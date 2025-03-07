﻿//******************************************************************************************************
//  eDNAGrafanaController.cs - Gbtc
//
//  Copyright © 2018, Grid Protection Alliance.  All Rights Reserved.
//
//  Licensed to the Grid Protection Alliance (GPA) under one or more contributor license agreements. See
//  the NOTICE file distributed with this work for additional information regarding copyright ownership.
//  The GPA licenses this file to you under the MIT License (MIT), the "License"; you may not use this
//  file except in compliance with the License. You may obtain a copy of the License at:
//
//      http://opensource.org/licenses/MIT
//
//  Unless agreed to in writing, the subject software distributed under the License is distributed on an
//  "AS-IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. Refer to the
//  License for the specific language governing permissions and limitations.
//
//  Code Modification History:
//  ----------------------------------------------------------------------------------------------------
//  12/20/2018 - Billy Ernest
//       Generated original version of source code.
//
//******************************************************************************************************

using GrafanaAdapters;
using GrafanaAdapters.DataSourceValueTypes;
using GrafanaAdapters.Functions;
using GrafanaAdapters.Model.Annotations;
using GrafanaAdapters.Model.Common;
using GrafanaAdapters.Model.Functions;
using GrafanaAdapters.Model.Metadata;
using GSF;
using GSF.Collections;
using GSF.Configuration;
using GSF.Diagnostics;
using GSF.Security;
using GSF.TimeSeries;
using GSF.Web.Security;
using InStep.eDNA.EzDNAApiNet;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Web.Http;
using GrafanaAdapters.Model.Database;
using eDNAMetaData = eDNAAdapters.Metadata;
using AlarmState = GrafanaAdapters.Model.Database.AlarmState;
using CancellationToken = System.Threading.CancellationToken;

// ReSharper disable VirtualMemberCallInConstructor
namespace openHistorian.eDNAGrafanaController
{
    /// <summary>
    /// Represents a REST based API for a simple JSON based Grafana data source for eDNA,
    /// accessible from Grafana data source as http://localhost:8180/api/ednagrafana
    /// </summary>
    public class eDNAGrafanaController : ApiController
    {
        #region [ Members ]

        // Nested Types

        /// <summary>
        /// Represents an eDNA data source for the Grafana adapter.
        /// </summary>
        [Serializable]
        protected class eDNADataSource : GrafanaDataSourceBase
        {
            /// <summary>
            /// eDNADataSource constructor
            /// </summary>
            public eDNADataSource()
            {
                DataTable dataTable = GetNewMetaDataTable();
                DataSet metadata = new DataSet();
                metadata.Tables.Add(dataTable);
                Metadata = metadata;
            }

            #region [ Methods ]

            /// <summary>
            /// Starts a query that will read data source values, given a set of point IDs and targets, over a time range.
            /// </summary>
            /// <param name="queryParameters">Parameters that define the query.</param>
            /// <param name="targetMap">Set of IDs with associated targets to query.</param>
            /// <param name="cancellationToken">Propagates notification from client that operations should be canceled.</param>
            /// <returns>Queried data source data in terms of value and time.</returns>
            protected override async IAsyncEnumerable<DataSourceValue> QueryDataSourceValues(QueryParameters queryParameters, OrderedDictionary<ulong, (string, string)> targetMap, [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                foreach ((string pointTag, string _) id in targetMap.Values)
                {
                    int[] expectedResults =
                    {
                        (int)eDNAHistoryReturnStatus.END_OF_HISTORY,
                        (int)eDNAHistoryReturnStatus.NO_HISTORY_FOR_TIME
                    };

                    int result = History.DnaGetHistRaw(id.pointTag, queryParameters.StartTime.ToLocalTime(), queryParameters.StopTime.ToLocalTime(), out uint key);

                    while (result == 0)
                    {
                        result = await new ValueTask<int>(History.DnaGetNextHist(key, out double value, out DateTime time, out string _));
                        DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, 0);

                        if (result == 0)
                            yield return new DataSourceValue()
                            {
                                ID = id,
                                Time = time.Subtract(epoch).TotalMilliseconds,
                                Value = value,
                                Flags = MeasurementStateFlags.Normal
                            };
                    }

                    // Assume that unexpected return status indicates an error
                    // and therefore the analysis results should be trusted
                    if (expectedResults.Contains(result))
                    {
                        Log.Publish(MessageLevel.Error, "eDNA Error", $"Unexpected eDNA return code: {result}");
                        break;
                    }
                }
            }

            #endregion  
        }

        #endregion

        #region [ Static ]

        private const string FileBackedDictionary = "eDNADataSources.bin";

        private static readonly LogPublisher Log = Logger.CreatePublisher(typeof(eDNAGrafanaController), MessageClass.Component);
        
        private static ConcurrentDictionary<string, eDNADataSource> DataSources { get; }

        static eDNAGrafanaController()
        {
            CategorizedSettingsElementCollection systemSettings = ConfigurationFile.Open("openHistorian.exe.config").Settings["systemSettings"];

            if (!systemSettings["eDNAGrafanaControllerEnabled", true]?.Value.ParseBoolean() ?? true)
                return;

            using (FileBackedDictionary<string, eDNADataSource> FileBackedDataSources = new FileBackedDictionary<string, eDNADataSource>(FileBackedDictionary))
                DataSources = new ConcurrentDictionary<string, eDNADataSource>(FileBackedDataSources);

            string eDNAMetaData = systemSettings["eDNAMetaData"]?.Value ?? "*.*";
            List<Task> tasks = new List<Task>();

            foreach (string setting in eDNAMetaData.Split(','))
            {
                string site = setting.Split('.')[0].ToUpper();
                string service = setting.Split('.')[1].ToUpper();

                if (!DataSources.ContainsKey($"{site}.{service}"))
                    DataSources.AddOrUpdate($"{site}.{service}", new eDNADataSource());

                tasks.Add(Task.Factory.StartNew(() => RefreshMetaData(site, service)));
            }

            Task.Factory.ContinueWhenAll(tasks.ToArray(), continuationTask =>
            {
                using FileBackedDictionary<string, eDNADataSource> FileBackedDataSources = new FileBackedDictionary<string, eDNADataSource>(FileBackedDictionary);
                
                foreach (KeyValuePair<string, eDNADataSource> kvp in DataSources) 
                    FileBackedDataSources[kvp.Key] = kvp.Value;
                
                FileBackedDataSources.Compact();
            });
        }

        private static void RefreshMetaData(string site, string service)
        {
            DataTable dataTable = GetNewMetaDataTable();
            IEnumerable<eDNAMetaData> results = eDNAMetaData.Query(new eDNAMetaData() { Site = site.ToUpper(), Service = service.ToUpper() });

            foreach (eDNAMetaData result in results)
            {
                if (result.ExtendedDescription == string.Empty)
                    continue;

                string fqn = $"{result.Site}.{result.Service}.{result.ShortID}";
                string id = $"edna_{result.Site}_{result.Service}:{result.ShortID}";

                try
                {
                    Guid nodeID = AdoSecurityProvider.DefaultNodeID;
                    Guid signalID = Guid.Empty;

                    if (!string.IsNullOrEmpty(result.LongID))
                        Guid.TryParse(result.LongID.Trim(), out signalID);

                    if (signalID == Guid.Empty)
                        signalID = Guid.NewGuid();

                    string tagName = result.ReferenceField01;

                    if (string.IsNullOrWhiteSpace(tagName))
                        tagName = result.Description;

                    if (!string.IsNullOrWhiteSpace(result.ReferenceField05))
                        id = result.ReferenceField05;

                    decimal latitude = 0.0M, longitude = 0.0M; 

                    if (!string.IsNullOrWhiteSpace(result.ReferenceField06))
                    {
                        string[] parts = result.ReferenceField06.Trim().Split(',');

                        if (parts.Length == 2)
                        {
                            decimal.TryParse(parts[0].Trim(), out latitude);
                            decimal.TryParse(parts[1].Trim(), out longitude);
                        }
                    }

                    string signalType = result.ReferenceField03?.Trim().ToUpperInvariant();
                    string phasorType = null;

                    if (!string.IsNullOrEmpty(signalType) && signalType.Length == 4)
                    {
                        phasorType = signalType switch
                        {
                            "VPHA" => "V",
                            "VPHM" => "V",
                            "IPHA" => "I",
                            "IPHM" => "I",
                            _ => phasorType
                        };
                    }
                    else
                    {
                        signalType = result.PointType switch
                        {
                            "DI" => "DIGI",
                            _ => "ALOG"
                        };
                    }

                    dataTable.Rows.Add(
                        nodeID,                     // Node ID
                        nodeID,                     // Source node
                        id,                         // ID
                        signalID,                   // Signal ID
                        fqn,                        // Point Tag
                        tagName,                    // Alternate Tag
                        result.ReferenceField02,    // Signal Ref
                        1,                          // Internal
                        0,                          // Subscribed
                        result.ReferenceField04,    // Device
                        0,                          // Device ID
                        1,                          // Frames per Second
                        result.ReferenceField08,    // Protocol
                        signalType,                 // Signal Type
                        result.Units,               // Engineering Units
                        0,                          // Phasor ID
                        phasorType,                 // Phasor Type
                        null,                       // Phase
                        0.0D,                       // Adder
                        1.0D,                       // Multiplier
                        result.ReferenceField07,    // Company
                        longitude,                  // Longitude
                        latitude,                   // Latitude
                        result.ExtendedDescription, // Description
                        DateTime.UtcNow             // Updated On
                    );
                }
                catch (Exception ex)
                {
                    Log.Publish(MessageLevel.Error, $"eDNA Controller Metadata load error for {site}.{service}", exception: ex);
                }
            }

            DataSet metaData = new DataSet();
            metaData.Tables.Add(dataTable);

            DataSources[$"{site.ToUpper()}.{service.ToUpper()}"].Metadata = metaData;
        }

        private static DataTable GetNewMetaDataTable()
        {
            DataTable dataTable = new DataTable("ActiveMeasurements");

            dataTable.Columns.Add("NodeID", typeof(Guid));
            dataTable.Columns.Add("SourceNodeID", typeof(Guid));
            dataTable.Columns.Add("ID", typeof(string));
            dataTable.Columns.Add("SignalID", typeof(Guid));
            dataTable.Columns.Add("PointTag", typeof(string));
            dataTable.Columns.Add("AlternateTag", typeof(string));
            dataTable.Columns.Add("SignalReference", typeof(string));
            dataTable.Columns.Add("Internal", typeof(int));
            dataTable.Columns.Add("Subscribed", typeof(int));
            dataTable.Columns.Add("Device", typeof(string));
            dataTable.Columns.Add("DeviceID", typeof(int));
            dataTable.Columns.Add("FramesPerSecond", typeof(int));
            dataTable.Columns.Add("Protocol", typeof(string));
            dataTable.Columns.Add("SignalType", typeof(string));
            dataTable.Columns.Add("EngineeringUnits", typeof(string));
            dataTable.Columns.Add("PhasorID", typeof(int));
            dataTable.Columns.Add("PhasorType", typeof(string));
            dataTable.Columns.Add("Phase", typeof(string));
            dataTable.Columns.Add("Adder", typeof(double));
            dataTable.Columns.Add("Multiplier", typeof(double));
            dataTable.Columns.Add("Company", typeof(string));
            dataTable.Columns.Add("Longitude", typeof(decimal));
            dataTable.Columns.Add("Latitude", typeof(decimal));
            dataTable.Columns.Add("Description", typeof(string));
            dataTable.Columns.Add("UpdatedOn", typeof(DateTime));

            return dataTable;
        }

        /// <summary>
        /// RefreshAllMetaData refreshes the metadata on command.
        /// </summary>
        public static void RefreshAllMetaData()
        {
            List<Task> tasks = new List<Task>();

            foreach (KeyValuePair<string, eDNADataSource> kvp in DataSources)
            {
                string site = kvp.Key.Split('.')[0].ToUpper();
                string service = kvp.Key.Split('.')[1].ToUpper();
                tasks.Add(Task.Factory.StartNew(() => RefreshMetaData(site, service)));

            }

            Task.Factory.ContinueWhenAll(tasks.ToArray(), continuationTask =>
            {
                using FileBackedDictionary<string, eDNADataSource> FileBackedDataSources = new FileBackedDictionary<string, eDNADataSource>(FileBackedDictionary);
                foreach (KeyValuePair<string, eDNADataSource> kvp in DataSources)
                {
                    FileBackedDataSources[kvp.Key] = kvp.Value;
                }
                FileBackedDataSources.Compact();
            });
        }

        #endregion

        #region [ Methods ]

        /// <summary>
        /// Validates that openHistorian Grafana data source is responding as expected.
        /// </summary>
        /// <param name="site">eDNA site name.</param>
        /// <param name="service">eDNA service name.</param>
        [HttpGet]
        public HttpResponseMessage Index(string site, string service)
        {
            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        /// <summary>
        /// Queries eDNA as a Grafana data source.
        /// </summary>
        /// <param name="site">eDNA site name.</param>
        /// <param name="service">eDNA service name.</param>
        /// <param name="request">Query request.</param>
        /// <param name="cancellationToken">Propagates notification from client that operations should be canceled.</param>
        [HttpPost]
        [SuppressMessage("Security", "SG0016", Justification = "Current operation dictated by Grafana. CSRF exposure limited to data access.")]
        public virtual Task<IEnumerable<TimeSeriesValues>> Query(string site, string service, QueryRequest request, CancellationToken cancellationToken)
        {
            if (request.targets.FirstOrDefault()?.target is null)
                return Task.FromResult(Enumerable.Empty<TimeSeriesValues>());

            if (!DataSources.ContainsKey($"{site.ToUpper()}.{service.ToUpper()}"))
                RefreshMetaData(site.ToUpper(), service.ToUpper());

            return DataSources[$"{site.ToUpper()}.{service.ToUpper()}"]?.Query(request, cancellationToken) ?? Task.FromResult(Enumerable.Empty<TimeSeriesValues>());
        }

        /// <summary>
        /// Gets the data source value types, i.e., any type that has implemented <see cref="IDataSourceValueType"/>,
        /// that have been loaded into the application domain.
        /// </summary>
        /// <param name="site">eDNA site name.</param>
        /// <param name="service">eDNA service name.</param>
        [HttpPost]
        public virtual IEnumerable<DataSourceValueType> GetValueTypes(string site, string service)
        {
            if (!DataSources.ContainsKey($"{site.ToUpper()}.{service.ToUpper()}"))
                RefreshMetaData(site.ToUpper(), service.ToUpper());

            return DataSources[$"{site.ToUpper()}.{service.ToUpper()}"]?.GetValueTypes() ?? Enumerable.Empty<DataSourceValueType>();
        }


        /// <summary>
        /// Gets the table names that, at a minimum, contain all the fields that the value type has defined as
        /// required, see <see cref="IDataSourceValueType.RequiredMetadataFieldNames"/>.
        /// </summary>
        /// <param name="site">eDNA site name.</param>
        /// <param name="service">eDNA service name.</param>
        /// <param name="request">Search request.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        [HttpPost]
        public virtual Task<IEnumerable<string>> GetValueTypeTables(string site, string service, SearchRequest request, CancellationToken cancellationToken)
        {
            if (!DataSources.ContainsKey($"{site.ToUpper()}.{service.ToUpper()}"))
                RefreshMetaData(site.ToUpper(), service.ToUpper());

            return DataSources[$"{site.ToUpper()}.{service.ToUpper()}"]?.GetValueTypeTables(request, cancellationToken) ?? Task.FromResult(Enumerable.Empty<string>());
        }

        /// <summary>
        /// Gets the field names for a given table.
        /// </summary>
        /// <param name="site">eDNA site name.</param>
        /// <param name="service">eDNA service name.</param>
        /// <param name="request">Search request.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        [HttpPost]
        public virtual Task<IEnumerable<FieldDescription>> GetValueTypeTableFields(string site, string service, SearchRequest request, CancellationToken cancellationToken)
        {
            if (!DataSources.ContainsKey($"{site.ToUpper()}.{service.ToUpper()}"))
                RefreshMetaData(site.ToUpper(), service.ToUpper());

            return DataSources[$"{site.ToUpper()}.{service.ToUpper()}"]?.GetValueTypeTableFields(request, cancellationToken) ?? Task.FromResult(Enumerable.Empty<FieldDescription>());
        }

        /// <summary>
        /// Gets the functions that are available for a given data source value type.
        /// </summary>
        /// <param name="site">eDNA site name.</param>
        /// <param name="service">eDNA service name.</param>
        /// <param name="request">Search request.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <remarks>
        /// <see cref="SearchRequest.expression"/> is used to filter functions by group operation, specifically a
        /// value of "None", "Slice", or "Set" as defined in the <see cref="GroupOperations"/> enumeration. If all
        /// function descriptions are desired, regardless of group operation, an empty string can be provided.
        /// Combinations are also supported, e.g., "Slice,Set".
        /// </remarks>
        [HttpPost]
        public virtual Task<IEnumerable<FunctionDescription>> GetValueTypeFunctions(string site, string service, SearchRequest request, CancellationToken cancellationToken)
        {
            if (!DataSources.ContainsKey($"{site.ToUpper()}.{service.ToUpper()}"))
                RefreshMetaData(site.ToUpper(), service.ToUpper());

            return DataSources[$"{site.ToUpper()}.{service.ToUpper()}"]?.GetValueTypeFunctions(request, cancellationToken) ?? Task.FromResult(Enumerable.Empty<FunctionDescription>());
        }

        /// <summary>
        /// Search openHistorian for a target.
        /// </summary>
        /// <param name="site">eDNA site name.</param>
        /// <param name="service">eDNA service name.</param>
        /// <param name="request">Search target.</param>
        /// <param name="cancellationToken">Propagates notification from client that operations should be canceled.</param>
        [HttpPost]
        public virtual Task<string[]> Search(string site, string service, SearchRequest request, CancellationToken cancellationToken)
        {
            if (!DataSources.ContainsKey($"{site.ToUpper()}.{service.ToUpper()}"))
                RefreshMetaData(site.ToUpper(), service.ToUpper());

            return DataSources[$"{site.ToUpper()}.{service.ToUpper()}"]?.Search(request, cancellationToken) ?? Task.FromResult(Array.Empty<string>());
        }

        /// <summary>
        /// Reloads data source value types cache.
        /// </summary>
        /// <param name="site">eDNA site name.</param>
        /// <param name="service">eDNA service name.</param>
        /// <remarks>
        /// This function is used to support dynamic data source value type loading. Function only needs to be called
        /// when a new data source value is added to Grafana at run-time and end-user wants to use newly installed
        /// data source value type without restarting host.
        /// </remarks>
        [HttpGet]
        [AuthorizeControllerRole("Administrator")]
        public virtual void ReloadValueTypes(string site, string service)
        {
            if (!DataSources.ContainsKey($"{site.ToUpper()}.{service.ToUpper()}"))
                RefreshMetaData(site.ToUpper(), service.ToUpper());

            DataSources[$"{site.ToUpper()}.{service.ToUpper()}"]?.ReloadDataSourceValueTypes();
        }

        /// <summary>
        /// Reloads Grafana functions cache.
        /// </summary>
        /// <param name="site">eDNA site name.</param>
        /// <param name="service">eDNA service name.</param>
        /// <remarks>
        /// This function is used to support dynamic loading for Grafana functions. Function only needs to be called
        /// when a new function is added to Grafana at run-time and end-user wants to use newly installed function
        /// without restarting host.
        /// </remarks>
        [HttpGet]
        [AuthorizeControllerRole("Administrator")]
        public virtual void ReloadGrafanaFunctions(string site, string service)
        {
            if (!DataSources.ContainsKey($"{site.ToUpper()}.{service.ToUpper()}"))
                RefreshMetaData(site.ToUpper(), service.ToUpper());

            DataSources[$"{site.ToUpper()}.{service.ToUpper()}"]?.ReloadGrafanaFunctions();
        }

        /// <summary>
        /// Queries openHistorian for alarm state.
        /// </summary>
        /// <param name="site">eDNA site name.</param>
        /// <param name="service">eDNA service name.</param>
        /// <param name="cancellationToken">Propagates notification from client that operations should be canceled.</param>
        [HttpPost]
        public virtual Task<IEnumerable<AlarmDeviceStateView>> GetAlarmState(string site, string service, CancellationToken cancellationToken)
        {
            if (!DataSources.ContainsKey($"{site.ToUpper()}.{service.ToUpper()}"))
                RefreshMetaData(site.ToUpper(), service.ToUpper());

            return DataSources[$"{site.ToUpper()}.{service.ToUpper()}"]?.GetAlarmState(cancellationToken) ?? Task.FromResult(Enumerable.Empty<AlarmDeviceStateView>());
        }

        /// <summary>
        /// Queries openHistorian for device alarms.
        /// </summary>
        /// <param name="site">eDNA site name.</param>
        /// <param name="service">eDNA service name.</param>
        /// <param name="cancellationToken">Propagates notification from client that operations should be canceled.</param>
        [HttpPost]
        public virtual Task<IEnumerable<AlarmState>> GetDeviceAlarms(string site, string service, CancellationToken cancellationToken)
        {
            if (!DataSources.ContainsKey($"{site.ToUpper()}.{service.ToUpper()}"))
                RefreshMetaData(site.ToUpper(), service.ToUpper());

            return DataSources[$"{site.ToUpper()}.{service.ToUpper()}"]?.GetDeviceAlarms(cancellationToken) ?? Task.FromResult(Enumerable.Empty<AlarmState>());
        }

        /// <summary>
        /// Queries openHistorian for device groups.
        /// </summary>
        /// <param name="site">eDNA site name.</param>
        /// <param name="service">eDNA service name.</param>
        /// <param name="cancellationToken">Propagates notification from client that operations should be canceled.</param>
        [HttpPost]
        public virtual Task<IEnumerable<DeviceGroup>> GetDeviceGroups(string site, string service, CancellationToken cancellationToken)
        {
            if (!DataSources.ContainsKey($"{site.ToUpper()}.{service.ToUpper()}"))
                RefreshMetaData(site.ToUpper(), service.ToUpper());

            return DataSources[$"{site.ToUpper()}.{service.ToUpper()}"]?.GetDeviceGroups(cancellationToken) ?? Task.FromResult(Enumerable.Empty<DeviceGroup>());
        }

        /// <summary>
        /// Queries openHistorian for annotations in a time-range (e.g., Alarms).
        /// </summary>
        /// <param name="site">eDNA site name.</param>
        /// <param name="service">eDNA service name.</param>
        /// <param name="request">Annotation request.</param>
        /// <param name="cancellationToken">Propagates notification from client that operations should be canceled.</param>
        [HttpPost]
        public virtual Task<List<AnnotationResponse>> Annotations(string site, string service, AnnotationRequest request, CancellationToken cancellationToken)
        {
            if (!DataSources.ContainsKey($"{site.ToUpper()}.{service.ToUpper()}"))
                RefreshMetaData(site.ToUpper(), service.ToUpper());

            return DataSources[$"{site.ToUpper()}.{service.ToUpper()}"]?.Annotations(request, cancellationToken) ?? Task.FromResult(new List<AnnotationResponse>());
        }

        #endregion
    }
}