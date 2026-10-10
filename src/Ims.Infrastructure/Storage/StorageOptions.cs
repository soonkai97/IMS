using System.ComponentModel.DataAnnotations;

namespace Ims.Infrastructure.Storage;

public class StorageOptions
{
    /// <summary>Folder where files are stored. Outside any web-served folder; back it up together with the database.</summary>
    [Required] public string RootPath { get; set; } = "";
}
