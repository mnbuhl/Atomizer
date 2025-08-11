namespace Atomizer.EntityFrameworkCore.Storage
{
    public class EntityFrameworkCoreJobStorageOptions
    {
        public string Schema { get; set; } = "Atomizer";
        public EntityFrameworkCoreStorageProvider StorageProvider { get; internal set; }
    }
}
