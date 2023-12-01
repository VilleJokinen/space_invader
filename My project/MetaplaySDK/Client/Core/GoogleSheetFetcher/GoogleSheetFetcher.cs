// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Config;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Metaplay.Core
{
    public struct GoogleSpreadsheetMetadata
    {
        public string Title { get; set; }
    }

    public interface IGoogleSheetFetcherImpl
    {
        Task<object> CredentialsFromFileAsync(string path, CancellationToken ct);
        Task<object> CredentialsFromUserInputAsync(string clientId, string clientSecret, CancellationToken ct);
        object CredentialsFromJsonString(string json);
        Task<IReadOnlyList<SpreadsheetContent>> FetchSheetsAsync(object credentials, string spreadsheetId, IEnumerable<string> sheetNames, CancellationToken ct);
        Task<GoogleSpreadsheetMetadata> FetchSpreadsheetMetadataAsync(object credentials, string spreadsheetId, CancellationToken ct);
    }

    public static partial class GoogleSheetFetcher
    {
        static Lazy<IGoogleSheetFetcherImpl> _implLazy = new Lazy<IGoogleSheetFetcherImpl>(FindImpl);
        public static IGoogleSheetFetcherImpl Instance => _implLazy.Value;

        static IGoogleSheetFetcherImpl FindImpl()
        {
            Type implType = TypeScanner.TryGetTypeByName("Metaplay.Core.GoogleSheetFetcherImpl");
            if (implType == null)
                throw new InvalidOperationException("GoogleSheetFetcher is only supported in server and editor builds!");
            IGoogleSheetFetcherImpl actualImpl = (IGoogleSheetFetcherImpl)Activator.CreateInstance(implType);
            return DebugEnableStaticFileCopy ? new StaticFileCopyFetcher(actualImpl) : actualImpl;
        }

        /// <summary>
        /// Read Google credentials from json string. Can be used to fetch Google Sheets with service accounts
        /// by generating a service account in a GCP project, giving the SA access to the desired sheet (using
        /// the Share button, and adding the SA's email with read-only access), and downloading the SA's
        /// credentials in json format.
        /// </summary>
        /// <param name="credentialsJsonString">Credentials json</param>
        /// <param name="spreadsheetId"></param>
        /// <param name="sheetNames"></param>
        /// <returns></returns>
        public static Task<IReadOnlyList<SpreadsheetContent>> FetchSheetsAsyncCredentialsString(string credentialsJsonString, string spreadsheetId, IEnumerable<string> sheetNames, CancellationToken ct)
        {
            object credentials = Instance.CredentialsFromJsonString(credentialsJsonString);
            return Instance.FetchSheetsAsync(credentials, spreadsheetId, sheetNames, ct);
        }

        /// <summary>
        /// Load Google credentials from .json file. Can be used to fetch Google Sheets with service accounts
        /// by generating a service account in a GCP project, giving the SA access to the desired sheet (using
        /// the Share button, and adding the SA's email with read-only access), and downloading the SA's
        /// credentials in json format.
        /// </summary>
        /// <param name="credentialsPath">Full path to the .json credentials file</param>
        /// <param name="spreadsheetId"></param>
        /// <param name="sheetNames"></param>
        /// <returns></returns>
        public static async Task<IReadOnlyList<SpreadsheetContent>> FetchSheetsAsync(string credentialsPath, string spreadsheetId, IEnumerable<string> sheetNames, CancellationToken ct)
        {
            object credentials = await Instance.CredentialsFromFileAsync(credentialsPath, ct);
            return await Instance.FetchSheetsAsync(credentials, spreadsheetId, sheetNames, ct);
        }

        public static async Task<SpreadsheetContent> FetchSheetAsync(string credentialsPath, string spreadsheetId, string sheetName, CancellationToken ct)
        {
            object credentials = await Instance.CredentialsFromFileAsync(credentialsPath, ct);
            return (await Instance.FetchSheetsAsync(credentials, spreadsheetId, Enumerable.Repeat(sheetName, 1), ct)).FirstOrDefault();
        }

        /// <summary>
        /// Fetch sheets using the user's own Google account.
        /// </summary>
        /// <param name="clientId"></param>
        /// <param name="clientSecret"></param>
        /// <param name="spreadsheetId"></param>
        /// <param name="sheetNames"></param>
        /// <returns></returns>
        public static async Task<IReadOnlyList<SpreadsheetContent>> FetchSheetsAsync(
            string clientId,
            string clientSecret,
            string spreadsheetId,
            IEnumerable<string> sheetNames,
            CancellationToken ct)
        {
            object credentials = await Instance.CredentialsFromUserInputAsync(clientId, clientSecret, ct);
            return await Instance.FetchSheetsAsync(credentials, spreadsheetId, sheetNames, ct);
        }
    }
}
