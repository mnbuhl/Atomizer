namespace Atomizer.EntityFrameworkCore.Storage
{
    public class EntityFrameworkCoreJobStorageOptions
    {
        public string Schema { get; set; } = "Atomizer";
        public EFCoreStorageProvider StorageProvider { get; internal set; }
    }
}
