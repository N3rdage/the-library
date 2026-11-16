namespace BookTracker.Data.Models;

// Which cover column a CoverMirrorState row tracks: Edition.CoverUrl or
// Book.DefaultCoverArtUrl.
public enum CoverMirrorTarget
{
    Edition = 0,
    Book = 1
}
