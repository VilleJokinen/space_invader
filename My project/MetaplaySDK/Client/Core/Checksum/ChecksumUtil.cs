// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Serialization;
using System;
#if UNITY_EDITOR
using System.IO;
#endif

namespace Metaplay.Core
{
    public static class ChecksumUtil
    {
        public static string PrintDifference<TModel>(LogChannel log, string firstName, TModel first, byte[] firstSerialized, string secondName, TModel second, byte[] secondSerialized)
        {
            string stateDiff;
            Exception compareException = null;
            try
            {
                // \note Resolve state diff last in case there's some case that causes it to crash (it's not super robust)
                stateDiff = PrettyPrinter.Difference<TModel>(first, second);
            }
            catch (Exception ex)
            {
                compareException = ex;
                stateDiff = $"Exception during compare: {ex}";
            }

            string serializedFirst = TaggedWireSerializer.ToString(firstSerialized);
            string serializedSecond = TaggedWireSerializer.ToString(secondSerialized);
            bool serializedAreIdentical = (serializedFirst == serializedSecond);

            if (compareException != null)
                log.Error("Failed to compare states between {A} and {B}: {Exception}", firstName, secondName, compareException);
            else if (stateDiff != "")
                log.Warning("State difference ({A} vs {B}): {Diff}", firstName, secondName, stateDiff);
            else if (serializedAreIdentical)
                log.Warning("No difference detected between states ({A} vs {B}) detected.", firstName, secondName);
            else
                log.Warning("Serialization difference ({A} vs {B}) detected, but resulting state is equal. This may be for example due to different member ordering, some members only being serialized in one version, etc. Please compare the serialized states below.", firstName, secondName);

#if UNITY_EDITOR
#pragma warning disable MP_WGL_00 // "Feature is poorly supported in WebGL". False positive, this is editor-only.
            // Dump states to files for comparison using external diff tools
            File.WriteAllText($"StateDump_{firstName}.txt",
                $"Full state of {firstName}:\n" + PrettyPrinter.Verbose(first) +
                $"\nSerialized state of {firstName}:\n" + serializedFirst);

            File.WriteAllText($"StateDump_{secondName}.txt",
                $"Full state of {secondName}:\n" + PrettyPrinter.Verbose(second) +
                $"\nSerialized state of {secondName}:\n" + serializedSecond);
#pragma warning restore MP_WGL_00
#endif

            // Print states to log
            log.Warning("Full {A} state: {State}", firstName, PrettyPrinter.Verbose(first));
            log.Warning("Full {B} state: {State}", secondName, PrettyPrinter.Verbose(second));

            // Print serialized states to log (in some cases, the model diff can be empty and serialized states still differ)
            log.Warning("Serialized {A} state: {SerializedState}", firstName, serializedFirst);
            log.Warning("Serialized {B} state: {SerializedState}", secondName, serializedSecond);

            return stateDiff;
        }
    }
}
