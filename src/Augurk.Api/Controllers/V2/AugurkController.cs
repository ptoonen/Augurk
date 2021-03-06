﻿/*
 Copyright 2017-2018, Augurk
 
 Licensed under the Apache License, Version 2.0 (the "License");
 you may not use this file except in compliance with the License.
 You may obtain a copy of the License at
 
 http://www.apache.org/licenses/LICENSE-2.0
 
 Unless required by applicable law or agreed to in writing, software
 distributed under the License is distributed on an "AS IS" BASIS,
 WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 See the License for the specific language governing permissions and
 limitations under the License.
*/

using Augurk.Api.Managers;
using System.Threading.Tasks;
using System.Web.Http;
using Augurk.Entities;
using System.Web.Http.Description;
using Raven.Abstractions.Smuggler;
using Raven.Smuggler;
using System.IO;
using System;
using Raven.Abstractions.Data;
using System.Net.Http;
using System.Net;
using System.Net.Http.Headers;
using System.Linq;
using System.Collections.Generic;
using Raven.Database.Smuggler;
using Raven.Client.Embedded;

namespace Augurk.Api.Controllers.V2
{
    /// <summary>
    /// ApiController for retrieving and persisting Augurk settings.
    /// </summary>
    [RoutePrefix("api/v2")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public class AugurkController : ApiController
    {
        private readonly CustomizationManager _customizationManager = new CustomizationManager();
        private readonly ConfigurationManager _configurationManager = new ConfigurationManager();

        /// <summary>
        /// Gets the customization settings.
        /// </summary>
        /// <returns>All customization related settings and their values.</returns>
        [Route("customization")]
        [HttpGet]
        public async Task<Customization> GetCustomizationAsync()
        {
            return await _customizationManager.GetOrCreateCustomizationSettingsAsync();
        }

        /// <summary>
        /// Pesists the provided customization settings.
        /// </summary>
        [Route("customization")]
        [HttpPut]
        [HttpPost]
        public async Task PersistCustomizationAsync(Customization customizationSettings)
        {
            await _customizationManager.PersistCustomizationSettingsAsync(customizationSettings);
        }

        /// <summary>
        /// Gets the configuration.
        /// </summary>
        /// <returns>All configuration.</returns>
        [Route("configuration")]
        [HttpGet]
        public async Task<Configuration> GetConfigurationAsync()
        {
            return await _configurationManager.GetOrCreateConfigurationAsync();
        }

        /// <summary>
        /// Pesists the provided configurations.
        /// </summary>
        [Route("configuration")]
        [HttpPut]
        [HttpPost]
        public async Task PersisConfigurationAsync(Configuration configuration)
        {
            await _configurationManager.PersistConfigurationAsync(configuration);
        }

        /// <summary>
        /// Imports existing data into Augurk.
        /// </summary>
        /// <returns></returns>
        [Route("import")]
        [HttpPost]
        public async Task<HttpResponseMessage> Import()
        {
            // Make sure that we actually got the right data
            if (!Request.Content.IsMimeMultipartContent())
            {
                throw new HttpResponseException(HttpStatusCode.UnsupportedMediaType);
            }

            try
            {
                // Store the uploaded file into a temporary location
                var provider = new MultipartFormDataStreamProvider(Path.GetTempPath());
                await Request.Content.ReadAsMultipartAsync(provider);

                string filename = provider.FormData.GetValues("filename").First();
                var file = provider.FileData.First();

                // Setup an import using RavenDb's Smuggler API or the DatabaseDumper API depending on whether the embedded database is being used
                SmugglerDatabaseApiBase importer;
                RavenConnectionStringOptions connectionStringOptions;
                if (Database.DocumentStore is EmbeddableDocumentStore embeddableDocumentStore)
                {
                    importer = new DatabaseDataDumper(embeddableDocumentStore.DocumentDatabase);
                    connectionStringOptions = new EmbeddedRavenConnectionStringOptions();
                }
                else
                {
                    importer = new SmugglerDatabaseApi();
                    connectionStringOptions = new RavenConnectionStringOptions()
                    {
                        Url = Database.DocumentStore.Url
                    };
                }

                var importOptions = new SmugglerImportOptions<RavenConnectionStringOptions>()
                {
                    FromFile = file.LocalFileName,
                    To = connectionStringOptions
                };

                await importer.ImportData(importOptions);

                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            catch (Exception exp)
            {
                return this.Request.CreateErrorResponse(HttpStatusCode.InternalServerError, exp);
            }
        }

        /// <summary>
        /// Exports data in Augurk to a file that can be used to import the data into another instance.
        /// </summary>
        /// <returns></returns>
        [Route("export")]
        [HttpGet]
        public async Task<HttpResponseMessage> Export()
        {
            try
            {
                // Setup an export using RavenDb's Smuggler API
                var exportTimestamp = DateTime.Now;
                var fileName = $"augurk-{exportTimestamp.ToString("yyyy-dd-M-HHmmss")}.bak";
                var options = new SmugglerDatabaseOptions
                {
                    OperateOnTypes = ItemType.Documents,
                    Filters = new List<FilterSetting>
                    {
                        new FilterSetting
                        {
                            Path = "@metadata.@id",
                            ShouldMatch = false,
                            Values = new List<string>
                            {
                                ConfigurationManager.DOCUMENT_KEY,
                                CustomizationManager.DOCUMENT_KEY,
                            }
                        }
                    }
                };

                // Determine the appropriate import method to use
                SmugglerDatabaseApiBase exporter;
                RavenConnectionStringOptions connectionStringOptions;
                if (Database.DocumentStore is EmbeddableDocumentStore embeddableDocumentStore)
                {
                    exporter = new DatabaseDataDumper(embeddableDocumentStore.DocumentDatabase, options);
                    connectionStringOptions = new EmbeddedRavenConnectionStringOptions();
                }
                else
                {
                    exporter = new SmugglerDatabaseApi(options);
                    connectionStringOptions = new RavenConnectionStringOptions()
                    {
                        Url = Database.DocumentStore.Url
                    };
                }

                var exportOptions = new SmugglerExportOptions<RavenConnectionStringOptions>()
                {
                    ToFile = Path.Combine(Path.GetTempPath(), fileName),
                    From = connectionStringOptions
                };

                // Perform the export
                await exporter.ExportData(exportOptions);

                // Stream the backup back to the client
                var result = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(File.ReadAllBytes(exportOptions.ToFile))
                };

                result.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
                {
                    FileName = fileName
                };

                result.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                return result;
            }
            catch
            {
                return Request.CreateErrorResponse(HttpStatusCode.InternalServerError, "An exception occured while generating export.");
            }
        }
    }
}
