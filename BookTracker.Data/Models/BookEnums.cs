namespace BookTracker.Data.Models;

// Ordinals are deliberately preserved across the v1 (Hardcopy/Softcopy) ->
// v2 (richer) transition: Hardcopy(0) -> Hardcover(0), Softcopy(1) ->
// TradePaperback(1). Existing rows therefore migrate without a SQL update;
// previously-Softcopy rows land as TradePaperback (the most common case),
// and can be re-classified manually or by the EditionFormatBackfillService
// startup task.
public enum BookFormat
{
    Hardcover = 0,
    TradePaperback = 1,
    MassMarketPaperback = 2,
    LargePrint = 3,
}

// Standard used-book grading scale, best to worst.
public enum BookCondition
{
    AsNew,
    Fine,
    VeryGood,
    Good,
    Fair,
    Poor
}
