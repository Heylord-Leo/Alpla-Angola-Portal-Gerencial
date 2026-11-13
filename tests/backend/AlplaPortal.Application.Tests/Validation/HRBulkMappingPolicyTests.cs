using System;
using AlplaPortal.Application.Validation;
using Xunit;
using P = AlplaPortal.Application.Validation.HRBulkMappingPolicy;

namespace AlplaPortal.Application.Tests.Validation;

/// <summary>
/// Pure rules of the HR directory "Mapeamento em lote" screen (docs/HR_DIRECTORY_BULK_MAPPING.md §3): fill-by-default with
/// explicit overwrite, company consistency of the FINAL (plant, department master) pair with the three mismatch reasons, and
/// suggestion confirmation limited to an ACTIONABLE suggestion with exactly one candidate plant. Company codes follow the
/// policy's company map: plants carry "APA" / "APS", department masters carry "AlplaPLASTICOS" / "AlplaSOPRO".
/// </summary>
public class HRBulkMappingPolicyTests
{
    private static readonly Guid ManagerA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid ManagerB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    private static P.CurrentState Empty() => new(null, null, null, null, null);

    private static P.Assignment Plan(int? plantId = null, string? plantCompany = null, int? deptId = null, string? deptCompany = null, Guid? manager = null,
        bool overwritePlant = false, bool overwriteDepartment = false, bool overwriteManager = false)
        => new(plantId, plantCompany, deptId, deptCompany, manager, overwritePlant, overwriteDepartment, overwriteManager);

    [Fact]
    public void Fill_by_default_writes_only_empty_fields_and_keeps_existing_values()
    {
        // Plant 1 (APA) already set; plan asks for plant 2 (APA), department 68 (AlplaPLASTICOS) and a manager.
        var current = new P.CurrentState(1, "APA", null, null, null);
        var o = P.EvaluateAssignment(current, Plan(2, "APA", 68, "AlplaPLASTICOS", ManagerA));

        Assert.Equal(P.RowChange, o.Status);
        Assert.Equal(1, o.PlantId);                       // kept, not overwritten
        Assert.Equal(68, o.DepartmentMasterId);
        Assert.Equal(ManagerA, o.ManagerUserId);
        Assert.Equal(new[] { P.FieldDepartment, P.FieldManager }, o.ChangedFields);
        Assert.Equal(new[] { P.FieldPlant }, o.PreservedFields);
    }

    [Fact]
    public void Overwrite_flags_replace_existing_values_field_by_field()
    {
        var current = new P.CurrentState(1, "APA", 68, "AlplaPLASTICOS", ManagerA);
        var o = P.EvaluateAssignment(current, Plan(2, "APA", 69, "AlplaPLASTICOS", ManagerB, overwritePlant: true, overwriteManager: true));

        Assert.Equal(P.RowChange, o.Status);
        Assert.Equal(2, o.PlantId); Assert.Equal(68, o.DepartmentMasterId); Assert.Equal(ManagerB, o.ManagerUserId);
        Assert.Equal(new[] { P.FieldPlant, P.FieldManager }, o.ChangedFields);
        Assert.Equal(new[] { P.FieldDepartment }, o.PreservedFields);
    }

    [Fact]
    public void Same_values_or_everything_preserved_is_unchanged_with_a_reason()
    {
        var current = new P.CurrentState(1, "APA", 68, "AlplaPLASTICOS", ManagerA);
        var same = P.EvaluateAssignment(current, Plan(1, "APA", 68, "AlplaPLASTICOS", ManagerA));
        Assert.Equal(P.RowUnchanged, same.Status); Assert.Equal(P.ReasonNoChange, same.ReasonCode); Assert.Empty(same.ChangedFields);

        var preserved = P.EvaluateAssignment(current, Plan(2, "APA", 69, "AlplaPLASTICOS", ManagerB));
        Assert.Equal(P.RowUnchanged, preserved.Status); Assert.Equal(P.ReasonNoChange, preserved.ReasonCode);
        Assert.Equal(3, preserved.PreservedFields.Count);
    }

    [Fact]
    public void Fields_outside_the_plan_are_never_touched()
    {
        var current = new P.CurrentState(1, "APA", 68, "AlplaPLASTICOS", null);
        var o = P.EvaluateAssignment(current, Plan(manager: ManagerA));
        Assert.Equal(P.RowChange, o.Status);
        Assert.Equal(1, o.PlantId); Assert.Equal(68, o.DepartmentMasterId); Assert.Equal(ManagerA, o.ManagerUserId);
        Assert.Equal(new[] { P.FieldManager }, o.ChangedFields); Assert.Empty(o.PreservedFields);
    }

    [Theory]
    // department-only onto an existing plant of another company
    [InlineData(1, "APA", null, null, null, null, 90, "AlplaSOPRO", false, false, "COMPANY_MISMATCH_EXISTING_PLANT")]
    // plant onto a kept department of another company (overwrite of the plant requested, department kept)
    [InlineData(null, null, 90, "AlplaSOPRO", 1, "APA", null, null, false, false, "COMPANY_MISMATCH_EXISTING_DEPARTMENT")]
    // both requested and inconsistent
    [InlineData(null, null, null, null, 1, "APA", 90, "AlplaSOPRO", false, false, "COMPANY_MISMATCH_REQUESTED")]
    public void Final_pair_of_another_company_is_blocked_with_the_matching_reason(
        int? curPlant, string? curPlantCo, int? curDept, string? curDeptCo,
        int? planPlant, string? planPlantCo, int? planDept, string? planDeptCo, bool owPlant, bool owDept, string expectedReason)
    {
        var current = new P.CurrentState(curPlant, curPlantCo, curDept, curDeptCo, null);
        var o = P.EvaluateAssignment(current, Plan(planPlant, planPlantCo, planDept, planDeptCo, null, owPlant, owDept));
        Assert.Equal(P.RowBlocked, o.Status);
        Assert.Equal(expectedReason, o.ReasonCode);
        Assert.False(string.IsNullOrWhiteSpace(o.Reason));
    }

    [Fact]
    public void A_legacy_mismatch_the_plan_does_not_touch_stays_allowed()
    {
        // Existing inconsistent pair (APA plant, AlplaSOPRO department); plan only sets the manager — as in the single save.
        var current = new P.CurrentState(1, "APA", 90, "AlplaSOPRO", null);
        var o = P.EvaluateAssignment(current, Plan(manager: ManagerA));
        Assert.Equal(P.RowChange, o.Status);
        Assert.Equal(new[] { P.FieldManager }, o.ChangedFields);
    }

    [Fact]
    public void Suggestion_confirmation_needs_an_actionable_suggestion_with_exactly_one_candidate()
    {
        var ok = P.EvaluateSuggestionConfirmation(Empty(), HRDirectoryMappingPolicy.SuggestionActionable, "APS", new[] { 3 }, "APS");
        Assert.Equal(P.RowChange, ok.Status); Assert.Equal(3, ok.PlantId); Assert.Equal(new[] { P.FieldPlant }, ok.ChangedFields);

        var alreadySet = P.EvaluateSuggestionConfirmation(new P.CurrentState(1, "APA", null, null, null), HRDirectoryMappingPolicy.SuggestionActionable, "APA", new[] { 1 }, "APA");
        Assert.Equal(P.RowUnchanged, alreadySet.Status); Assert.Equal(P.ReasonPlantAlreadySet, alreadySet.ReasonCode);

        var needsChoice = P.EvaluateSuggestionConfirmation(Empty(), HRDirectoryMappingPolicy.SuggestionNeedsChoice, "APA", new[] { 1, 2 }, "APA");
        Assert.Equal(P.RowBlocked, needsChoice.Status); Assert.Equal(P.ReasonSuggestionNotActionable, needsChoice.ReasonCode);
        Assert.Contains("mais do que uma planta", needsChoice.Reason);

        foreach (var state in new[] { HRDirectoryMappingPolicy.SuggestionManual, HRDirectoryMappingPolicy.SuggestionUnverified, HRDirectoryMappingPolicy.SuggestionNone })
        {
            var blocked = P.EvaluateSuggestionConfirmation(Empty(), state, null, Array.Empty<int>(), null);
            Assert.Equal(P.RowBlocked, blocked.Status); Assert.Equal(P.ReasonSuggestionNotActionable, blocked.ReasonCode);
        }

        // ACTIONABLE but two candidates (defensive): never decides between plants of one company.
        var two = P.EvaluateSuggestionConfirmation(Empty(), HRDirectoryMappingPolicy.SuggestionActionable, "APA", new[] { 1, 2 }, "APA");
        Assert.Equal(P.RowBlocked, two.Status);
    }

    [Fact]
    public void Suggested_plant_of_another_company_than_the_existing_department_is_blocked()
    {
        var current = new P.CurrentState(null, null, 90, "AlplaSOPRO", null);
        var o = P.EvaluateSuggestionConfirmation(current, HRDirectoryMappingPolicy.SuggestionActionable, "APA", new[] { 1 }, "APA");
        Assert.Equal(P.RowBlocked, o.Status); Assert.Equal(P.ReasonCompanyMismatchExistingDepartment, o.ReasonCode);
    }

    [Theory]
    [InlineData("ASSIGN", true)]
    [InlineData("CONFIRM_SUGGESTIONS", true)]
    [InlineData("assign", false)]
    [InlineData(null, false)]
    public void Only_the_two_modes_are_known(string? mode, bool expected) => Assert.Equal(expected, P.IsKnownMode(mode));
}
