namespace Api.Models;

public class PartImage
{
    public int PartImageId { get; set; }

    public int PartId { get; set; }
    public Part? Part { get; set; }

    public string FilePath { get; set; } = string.Empty; // served under /assets/parts/...
    public string FileName { get; set; } = string.Empty; // original filename, for display
    public int SortOrder { get; set; }

    public DateTime UploadedAt { get; set; } = DateTime.Now;
}
