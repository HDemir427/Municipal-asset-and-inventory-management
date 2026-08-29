using FluentValidation;

namespace MAIMS.Services.Validation;

/// <summary>
/// Validation rules for AssetCreateDto. Enforces required fields, length limits,
/// and ranges consistent with the DB schema.
/// </summary>
public class AssetCreateValidator : AbstractValidator<MAIMS.Core.DTOs.AssetCreateDto>
{
    public AssetCreateValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.CategoryId).GreaterThan(0);
        RuleFor(x => x.DepartmentId).GreaterThan(0);
        RuleFor(x => x.AcquisitionCost).GreaterThanOrEqualTo(0).When(x => x.AcquisitionCost.HasValue);
        RuleFor(x => x.SerialNumber).MaximumLength(80);
        RuleFor(x => x.FundingSource).MaximumLength(80);
    }
}

public class AssetUpdateValidator : AbstractValidator<MAIMS.Core.DTOs.AssetUpdateDto>
{
    public AssetUpdateValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(300);
        RuleFor(x => x.CategoryId).GreaterThan(0);
        RuleFor(x => x.DepartmentId).GreaterThan(0);
    }
}

public class AssetDisposalValidator : AbstractValidator<MAIMS.Core.DTOs.AssetDisposalDto>
{
    public AssetDisposalValidator()
    {
        RuleFor(x => x.AssetId).GreaterThan(0);
        RuleFor(x => x.ApprovedByUserId).GreaterThan(0);
        RuleFor(x => x.DisposalDate).LessThanOrEqualTo(DateTime.Today.AddDays(1));
        RuleFor(x => x.Proceeds).GreaterThanOrEqualTo(0).When(x => x.Proceeds.HasValue);
    }
}

public class AssetTransferValidator : AbstractValidator<MAIMS.Core.DTOs.AssetTransferDto>
{
    public AssetTransferValidator()
    {
        RuleFor(x => x.AssetId).GreaterThan(0);
        RuleFor(x => x.ApprovedByUserId).GreaterThan(0);
        RuleFor(x => x).Must(x => x.ToDepartmentId.HasValue || x.ToLocationId.HasValue || x.ToCustodianUserId.HasValue)
            .WithMessage("At least one of ToDepartmentId, ToLocationId, ToCustodianUserId must be set.");
    }
}
