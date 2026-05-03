namespace Atomizer.Models.Base;

/// <summary>
/// Base class for all Atomizer domain entities, providing a unique identifier.
/// </summary>
public abstract class Model
{
    /// <summary>
    /// Gets or sets the unique identifier for this entity.
    /// </summary>
    public Guid Id { get; set; }
}
