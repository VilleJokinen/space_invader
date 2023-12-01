// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if UNITY_2017_1_OR_NEWER
    #define ENABLE_SHEET_FETCHER_DEBUG_LOGGING
#endif

using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Metaplay.Core.Config;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
#if UNITY_2017_1_OR_NEWER
using Google.Apis.Util.Store;
using UnityEngine;
#endif

namespace Metaplay.Core
{
    public class GoogleSheetFetcherImpl : IGoogleSheetFetcherImpl
    {
        readonly static string[] Scopes = { SheetsService.Scope.SpreadsheetsReadonly };
        readonly static string AppName = "Metaplay Sheet Fetcher";

        public class FetchFailureException : Exception
        {
            string _detailMessage;

            public FetchFailureException(string detailMessage, Exception innerException) : base("Failed to fetch Google Sheet", innerException)
            {
                _detailMessage = detailMessage;
            }

            public override string ToString()
            {
                if (_detailMessage == null)
                    return $"{Message}\n{InnerException}";
                else
                    return $"{Message}: {_detailMessage}\n{InnerException}";
            }
        }

        public async Task<IReadOnlyList<SpreadsheetContent>> FetchSheetsAsync(object credentials, string spreadsheetId, IEnumerable<string> sheetNames, CancellationToken ct)
        {
            // Create Google Sheets API service.
            SheetsService service = new SheetsService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = (ICredential)credentials,
                ApplicationName = AppName,
            });

            // Fetch all sheets in one batch request.
            List<string> sheetNamesList = sheetNames.ToList();
#if ENABLE_SHEET_FETCHER_DEBUG_LOGGING
            DebugLog.Info("Fetching sheets: {SheetNames}...", string.Join(", ", sheetNamesList));
#endif
            SpreadsheetsResource.ValuesResource.BatchGetRequest request = service.Spreadsheets.Values.BatchGet(spreadsheetId);
            request.Ranges = sheetNamesList;
            BatchGetValuesResponse response;

            SpreadsheetsResource.GetRequest getRequest = service.Spreadsheets.Get(spreadsheetId);
            Spreadsheet                     getResponse;
            try
            {
                getResponse = await getRequest.ExecuteAsync(ct);
                response = await request.ExecuteAsync(ct);
            }
            catch (GoogleApiException ex)
            {
                if (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
                    throw new FetchFailureException("The specified sheet is not found.", ex);

                string missingSheetPageErrorPrefix = "Unable to parse range: ";
                if (ex.HttpStatusCode == System.Net.HttpStatusCode.BadRequest && ex.Error != null && ex.Error.Message != null && ex.Error.Message.StartsWith(missingSheetPageErrorPrefix, StringComparison.Ordinal))
                {
                    string sheetName = ex.Error.Message.Substring(missingSheetPageErrorPrefix.Length);
                    throw new FetchFailureException($"The specified sheet '{sheetName}' does not exist", ex);
                }

                if (ex.HttpStatusCode == System.Net.HttpStatusCode.Forbidden)
                    throw new FetchFailureException("No permission to access the specified sheet.", ex);

                throw new FetchFailureException(null, ex);
            }
            catch (Exception ex)
            {
                throw new FetchFailureException(null, ex);
            }

            // Throw if cancelled
            ct.ThrowIfCancellationRequested();

            // Check that we got the requested number of sheets in response
            int numSheets = response.ValueRanges?.Count ?? 0;
            if (numSheets != sheetNamesList.Count)
                throw new InvalidDataException($"Google Sheets API returned {response.ValueRanges?.Count} sheets, expecting {sheetNamesList.Count}");

            // Process sheets
            List<SpreadsheetContent> results = new List<SpreadsheetContent>();
            for (int sheetNdx = 0; sheetNdx < numSheets; sheetNdx++)
            {
                string          sheetName       = sheetNamesList[sheetNdx];
                SheetProperties sheetProperties = getResponse.Sheets.FirstOrDefault(x => x.Properties.Title.Equals(sheetName, StringComparison.InvariantCultureIgnoreCase))?.Properties;

                if(sheetProperties == null)
                    throw new InvalidDataException($"Unable to fetch sheet properties, no data returned");

                // \note: Looks like this should never be null but for some reason it is nullable
                int             sheetId         = sheetProperties.SheetId ?? 0;
                ValueRange      sheet           = response.ValueRanges[sheetNdx];

                IList<IList<object>> values = sheet.Values;
                if (values == null || values.Count == 0)
                    throw new InvalidDataException($"Unable to fetch sheet {sheetName}, no data returned");

                // Convert to List<List<string>>
                List<List<string>> cells = new List<List<string>>();
                foreach (IList<object> row in values)
                    cells.Add(row.Select(cell => (string)cell).ToList());

                results.Add(new SpreadsheetContent(sheetName, cells, new GoogleSheetSourceInfo(spreadsheetId, sheetName, sheetId)));
            }

            return results;
        }

        public async Task<GoogleSpreadsheetMetadata> FetchSpreadsheetMetadataAsync(object credentials, string spreadsheetId, CancellationToken ct)
        {
            // Create Google Sheets API service.
            SheetsService service = new SheetsService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = (ICredential)credentials,
                ApplicationName       = AppName,
            });

            try
            {
                SpreadsheetsResource.GetRequest getRequest = service.Spreadsheets.Get(spreadsheetId);
                Spreadsheet res = await getRequest.ExecuteAsync(ct);
                return new GoogleSpreadsheetMetadata { Title = res.Properties.Title };
            }
            catch (GoogleApiException ex)
            {
                if (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
                    throw new FetchFailureException("The specified sheet is not found.", ex);

                if (ex.HttpStatusCode == System.Net.HttpStatusCode.Forbidden)
                    throw new FetchFailureException("No permission to access the specified sheet.", ex);

                throw new FetchFailureException(null, ex);
            }
            catch (Exception ex)
            {
                throw new FetchFailureException(null, ex);
            }
        }

        public async Task<object> CredentialsFromFileAsync(string credentialsPath, CancellationToken ct)
        {
            return (await GoogleCredential.FromFileAsync(credentialsPath, ct)).CreateScoped(Scopes);
        }

        public object CredentialsFromJsonString(string credentialsJsonString)
        {
            return GoogleCredential.FromJson(credentialsJsonString).CreateScoped(Scopes);
        }

        #if UNITY_2017_1_OR_NEWER
        public async Task<object> CredentialsFromUserInputAsync(string clientId, string clientSecret, CancellationToken ct)
        {
            // Ensure Secrets directory exists, for storing auth tokens
            string tokenDir = Application.isEditor ? "Secrets" : Path.Combine(Application.dataPath, "Secrets");
            if (!Directory.Exists(tokenDir))
                Directory.CreateDirectory(tokenDir);

            // Create secrets object
            ClientSecrets secrets = new ClientSecrets
            {
                ClientId = clientId,
                ClientSecret = clientSecret,
            };

            // The file token.json stores the user's access and refresh tokens, and is created
            // automatically when the authorization flow completes for the first time.
            return await GoogleWebAuthorizationBroker.AuthorizeAsync(
                secrets,
                Scopes,
                "user",
                CancellationToken.None,
                new FileDataStore(tokenDir, true));
        }
        #else
        public Task<object> CredentialsFromUserInputAsync(string clientId, string clientSecret, CancellationToken ct)
        {
            throw new NotImplementedException("Getting Google credentials from user only supported in editor");
        }
        #endif
    }
}
