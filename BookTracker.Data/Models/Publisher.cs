using System.ComponentModel.DataAnnotations;

namespace BookTracker.Data.Models;

// TODO: add a "Manage publishers" feature — UI to rename/merge duplicates,
// delete unused publishers, and see which editions each publisher covers.
// Right now Publishers are only created via find-or-create on the Add page.
public class Publisher
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    public List<Edition> Editions { get; set; } = [];
}
