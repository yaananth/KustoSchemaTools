namespace KustoSchemaTools.Model
{
    public class FollowerPermissions
    {
        public FollowerModificationKind ModificationKind { get; set; }
        public List<AADObject> Viewers { get; set; } = new List<AADObject>();
        public List<AADObject> Admins { get; set; } = new List<AADObject>();

        /// <summary>
        /// Optional leader/follower name as known to the cluster. Some follower commands
        /// (e.g. .add follower database ... ) allow/require the leader name. We keep it
        /// here in case future YAML/metadata provides it. Not currently required for
        /// .set follower database operations.
        /// </summary>
        public string? LeaderName { get; set; }
    }

}
