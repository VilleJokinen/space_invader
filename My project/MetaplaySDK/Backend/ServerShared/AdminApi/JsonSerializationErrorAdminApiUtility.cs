// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Json;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Linq;
using System.Runtime.Serialization;

namespace Metaplay.Server.AdminApi
{
    public static class JsonSerializationErrorAdminApiUtility
    {
        /// <summary>
        /// Creates a <see cref="JsonSerializer"/> with the given <paramref name="settings"/> and <paramref name="errorLogger"/>.
        /// Errors are captured into <paramref name="errorLogger"/> and marked as handled, the serializer skips the offending property and continues serialization.
        /// </summary>
        public static JsonSerializer CreateSerializerWithJsonErrors(JsonSerializationErrorLogger errorLogger, JsonSerializerSettings settings)
        {
            JsonSerializerSettings serializerSettings = new JsonSerializerSettings(settings);
            serializerSettings.Context = new StreamingContext(StreamingContextStates.All, errorLogger);
            serializerSettings.Error   = JsonSerializationErrorUtility.HandleAdminApiJsonError;

            return JsonSerializer.Create(serializerSettings);
        }

        public static void WriteErrorsToConsole(JsonSerializationErrorLogger errorLogger, ILogger logger, string requestRoute)
        {
            if (errorLogger.Errors.Count == 0)
                return;

            logger.LogWarning($"AdminApi JSON serialization in endpoint '{requestRoute}' encountered {errorLogger.Errors.Count} (de-duplicating and logging the first 10) exceptions.");
            foreach (Exception ex in errorLogger.Errors.DistinctBy(x => x.Message).Take(10))
                logger.LogWarning(ex, "AdminApi JSON serialization ran into an exception");
        }
    }
}
