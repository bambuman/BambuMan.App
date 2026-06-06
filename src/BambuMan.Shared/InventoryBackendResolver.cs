using BambuMan.Shared.Enums;

namespace BambuMan.Shared
{
    /// <summary>
    /// Factory over the available inventory backend managers. The UI resolves the active backend by
    /// <see cref="InventoryBackend"/> and iterates <see cref="All"/> for cross-backend setup (init, subscribe).
    /// Adding a backend is: new enum value + new <see cref="BaseManager"/> subclass + DI registration.
    /// </summary>
    public interface IInventoryBackendResolver
    {
        IReadOnlyList<BaseManager> All { get; }

        BaseManager Resolve(InventoryBackend backend);
    }

    public class InventoryBackendResolver(IEnumerable<BaseManager> managers) : IInventoryBackendResolver
    {
        public IReadOnlyList<BaseManager> All { get; } = managers.ToList();

        public BaseManager Resolve(InventoryBackend backend) => All.First(m => m.Backend == backend);
    }
}
