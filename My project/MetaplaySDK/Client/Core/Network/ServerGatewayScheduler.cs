// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Message;
using System.Linq;

namespace Metaplay.Core.Network
{
    /// <summary>
    /// Selects the used gateways to the server backend. Implements backup gateway selection logic.
    /// </summary>
    public static class ServerGatewayScheduler
    {
        public static ServerGateway SelectGatewayForInitialConnection(ServerEndpoint backend, int numFailedConnectionAttempts)
        {
            int numAnomalies = numFailedConnectionAttempts;
            return SelectGatewayWithAnomalyCount(backend, numAnomalies);
        }

        public static ServerGateway SelectGatewayForConnectionResume(ServerEndpoint backend, ServerGateway previousGateway, int numFailedResumeAttempts, int numSuccessfulSessionResumes)
        {
            int numAnomalies = numFailedResumeAttempts;

            // one successful resume is completely ok. But any larger amount should trigger alt-endpoint selection.
            if (numSuccessfulSessionResumes > 1)
                numAnomalies += numSuccessfulSessionResumes - 1;

            // If no anomalies, use the same gateway as the previous connection.
            if (numAnomalies == 0)
                return previousGateway;

            return SelectGatewayWithAnomalyCount(backend, numAnomalies);
        }

        static ServerGateway SelectGatewayWithAnomalyCount(ServerEndpoint backend, int numAnomalies)
        {
            // choose gateway.
            // * First query is always to PrimaryGateway
            // * Second query is always to some BackupGateway, if there is any
            // * The next queries choose randomly from PrimaryGateway & BackupGateway

            ServerGateway gateway;
            if (numAnomalies == 0 || !backend.BackupGateways.Any())
            {
                gateway = backend.PrimaryGateway;
            }
            else
            {
                RandomPCG rng = RandomPCG.CreateNew();
                int numBackupGateways = backend.BackupGateways.Count();
                int chosenNdx = rng.NextInt(numAnomalies > 1 ? 1 + numBackupGateways : numBackupGateways);

                if (chosenNdx == numBackupGateways)
                    gateway = backend.PrimaryGateway;
                else
                    gateway = backend.BackupGateways.ElementAt(chosenNdx);
            }

            // Fix hostname:
            // * Use 127.0.0.1 instead of localhost, as localhost causes exception at EndConnect()

            if (gateway.ServerHost == "localhost")
                return new ServerGateway("127.0.0.1", gateway.ServerPort, gateway.EnableTls);
            else
                return gateway;
        }
    }
}
