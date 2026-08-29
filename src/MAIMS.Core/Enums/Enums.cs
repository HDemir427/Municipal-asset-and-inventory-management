namespace MAIMS.Core.Enums;

/// <summary>
/// Lifecycle status of a fixed asset.
/// Transition order: Planned → Acquired → In Service → Under Maintenance → In Storage → Disposed → Written Off.
/// </summary>
public enum AssetStatus
{
    Planned = 0,
    Acquired = 1,
    InService = 2,
    UnderMaintenance = 3,
    InStorage = 4,
    Disposed = 5,
    WrittenOff = 6
}

/// <summary>
/// Five-point condition rating for assets and inspections.
/// Avoids red/green-only coding for accessibility (AA contrast).
/// </summary>
public enum ConditionRating
{
    Critical = 1,
    Poor = 2,
    Fair = 3,
    Good = 4,
    Excellent = 5
}

/// <summary>
/// Top-level classification of fixed assets. Each category drives depreciation policy.
/// </summary>
public enum AssetCategoryType
{
    Land = 0,
    Buildings = 1,
    Infrastructure = 2,
    Vehicles = 3,
    Equipment = 4,
    FurnitureAndFixtures = 5,
    ITHardware = 6,
    Software = 7
}

/// <summary>
/// Type of lifecycle event recorded against an asset.
/// </summary>
public enum AssetEventType
{
    Acquisition = 0,
    StatusChange = 1,
    Transfer = 2,
    Maintenance = 3,
    Inspection = 4,
    ConditionChange = 5,
    Disposal = 6,
    WriteOff = 7,
    ValuationUpdate = 8
}

/// <summary>
/// Method used to dispose of an asset.
/// </summary>
public enum DisposalMethod
{
    Sale = 0,
    Donation = 1,
    Scrap = 2,
    TradeIn = 3,
    Loss = 4
}

/// <summary>
/// Type of stock movement. Each transaction type has specific required fields.
/// </summary>
public enum StockTransactionType
{
    Receipt = 0,
    Issue = 1,
    Transfer = 2,
    Adjustment = 3,
    WriteOff = 4,
    Reservation = 5,
    ReservationRelease = 6
}

/// <summary>
/// Mandatory reason codes for adjustments and write-offs.
/// Stored as string in DB to allow extensibility without migration.
/// </summary>
public static class StockReasonCodes
{
    public const string Damage = "DAMAGE";
    public const string Loss = "LOSS";
    public const string CountCorrection = "COUNT_CORRECTION";
    public const string Expired = "EXPIRED";
    public const string CountAdjustment = "COUNT_ADJUSTMENT";
    public const string Obsolete = "OBSOLETE";
}

/// <summary>
/// Unit of measure for inventory items.
/// </summary>
public enum UnitOfMeasure
{
    EA = 0,
    BOX = 1,
    L = 2,
    KG = 3,
    M = 4,
    M2 = 5,
    M3 = 6,
    ROLL = 7,
    PKT = 8
}

/// <summary>
/// Account status for users. Inactive users cannot log in.
/// </summary>
public enum UserStatus
{
    Active = 0,
    Inactive = 1,
    Locked = 2
}

/// <summary>
/// Audit log action type. Stored as a short string in DB.
/// </summary>
public static class AuditActions
{
    public const string Create = "CREATE";
    public const string Update = "UPDATE";
    public const string Delete = "DELETE";
    public const string Login = "LOGIN";
    public const string Logout = "LOGOUT";
    public const string FailedLogin = "FAILED_LOGIN";
}
