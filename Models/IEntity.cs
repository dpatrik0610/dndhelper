using System;

namespace dndhelper.Models
{
    /// <summary>
    /// Standard contract for all persistable database models, ensuring unified properties
    /// for unique identity (Id), auditing (CreatedAt, UpdatedAt), and soft-delete (IsDeleted) states.
    /// This enables generic repository constraints and automated lifecycle tracking.
    /// </summary>
    public interface IEntity
    {
        string? Id { get; set; }
        DateTime? CreatedAt { get; set; }
        DateTime? UpdatedAt { get; set; }
        bool IsDeleted { get; set; }  // soft delete flag
    }
}
