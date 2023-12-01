// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

namespace Metaplay.Cloud.Entity.EntityStatusMessages
{
    /// <summary>
    /// The phase of the entity lifecycle. Follows the following state machine:
    /// <code> <![CDATA[
    ///   Starting
    ///     |   |
    ///  .--'   |
    ///  |      |
    ///  |   Running <--> Suspended
    ///  |      |           |
    ///  '-----.|.----------'
    ///         |
    ///         V
    ///      Stopping
    /// ]]></code>
    /// </summary>
    public enum EntityStatus
    {
        /// <summary> Entity is being started and not ready to receive messages yet </summary>
        Starting,

        /// <summary> Entity is running normally </summary>
        Running,

        /// <summary> Entity is running but no messages are (temporarily) delivered </summary>
        Suspended,

        /// <summary> Entity is shutting down and cannot receive messages anymore </summary>
        Stopping
    }

    // The protocol:
    //
    //   [Shard]                          [Entity]
    //
    //
    // init:
    //
    //   Starting
    //      |
    //      |
    //      |    (spawn)
    //      |-------------------------> construct actor
    //      |                                |
    //      |    InitializeEntity            |
    //      |-------------------------> Initialize()
    //      |                                |
    //      |                 EntityReady    |
    //      |  <-----------------------------|
    //      |                                |
    //    Running                            |
    //      |                                |
    //      |                                |
    //
    // shutdown:
    //
    //      *                                *
    //      |       EntityShutdownRequest    |
    //      |  <-----------------------------|
    //      |                                |
    //    Stopping                           |
    //      |                                |
    //   (throttling queue)                  |
    //      |                                |
    //      |    ShutdownEntity              |
    //      |----------------------------->  |
    //      |                            OnShutdown()
    //      |                                |
    //      |                                |
    //      |     (Terminated)               |
    //      |  <----------------------------die
    //      |
    //    (Removed)
    //
    // suspension:
    //
    //    Running                            *
    //      |        EntitySuspendRequest    |
    //      |  <-----------------------------|
    //      |                                |
    //    Suspended                          |
    //      |                                |
    //      |    EntitySuspendEvent          |
    //      |----------------------------->  |
    //      |                                |
    //      |                               ...
    //      |                                |
    //      |         EntityResumeRequest    |
    //      |  <-----------------------------|
    //      |                                |
    //    Running

    /// <summary>
    /// Shard -> Entity. Request to run Initialize() (initial actor state).
    /// </summary>
    public class InitializeEntity
    {
        public static readonly InitializeEntity Instance = new InitializeEntity();
    }

    /// <summary>
    /// Entity -> Shard. Notify to transition to Running state (from Starting). Initialization was completed.
    /// </summary>
    public class EntityReady
    {
        public static readonly EntityReady Instance = new EntityReady();
    }

    /// <summary>
    /// Entity -> Shard. Request to transition to Stopping state (from any state).
    /// </summary>
    public class EntityShutdownRequest
    {
        public static readonly EntityShutdownRequest Instance = new EntityShutdownRequest();
    }

    /// <summary>
    /// Entity -> Shard. Request to transition to Suspended state (from Running state)
    /// </summary>
    public class EntitySuspendRequest
    {
        public static readonly EntitySuspendRequest Instance = new EntitySuspendRequest();
    }

    /// <summary>
    /// Entity -> Shard. Request to transition to Running state (from Suspended state)
    /// </summary>
    public class EntityResumeRequest
    {
        public static readonly EntityResumeRequest Instance = new EntityResumeRequest();
    }

    /// <summary>
    /// Shard -> Entity. Notification that the entity is now in Stopping state and it should shut down. No new messages will be delivered.
    /// </summary>
    public class ShutdownEntity
    {
        public static readonly ShutdownEntity Instance = new ShutdownEntity();
    }

    /// <summary>
    /// Shard -> Entity. Notification that the entity is now in Suspended state. No new messages will be delivered until entity resumes.
    /// </summary>
    public class EntitySuspendEvent
    {
        public static readonly EntitySuspendEvent Instance = new EntitySuspendEvent();
    }
}
