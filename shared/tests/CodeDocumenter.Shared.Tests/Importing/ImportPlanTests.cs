using CodeDocumenter.Shared.Importing;

namespace CodeDocumenter.Shared.Tests.Importing;

/// <summary>What a choice of items comes to: what it brings, what it relies on, and what may be wanted too.</summary>
public class ImportPlanTests
{
  static ImportPlan Plan(Dictionary<string, string[]> uses, string[] picks, Dictionary<string, ImportNeed[]>? needs = null, Dictionary<string, string[]>? offers = null) =>
    ImportPlan.Build(picks, id => uses.GetValueOrDefault(id, []), id => needs?.GetValueOrDefault(id, []) ?? [], offers is null ? null : id => offers.GetValueOrDefault(id, []));

  [Fact]
  public void What_the_picks_use_comes_along_to_the_end_of_each_chain_and_says_what_needs_it()
  {
    var plan = Plan(new() { ["Paths"] = ["Files"], ["Files"] = ["Logger"], ["Other"] = ["Unrelated"] }, ["Paths"]);

    Assert.Equal(["Paths"], plan.Picked);
    Assert.Equal(["Files", "Logger"], plan.Brought.Select(b => b.Id));
    Assert.Equal(["Paths"], plan.Brought[0].UsedBy);
    Assert.Equal(["Files"], plan.Brought[1].UsedBy);
    Assert.Equal(["Paths", "Files", "Logger"], plan.Items);
  }

  [Fact]
  public void A_cycle_is_followed_once_and_something_picked_isnt_also_brought()
  {
    var plan = Plan(new() { ["A"] = ["B"], ["B"] = ["A", "B", "C"] }, ["A", "B", "a"]);

    Assert.Equal(["A", "B"], plan.Picked);
    Assert.Equal(["C"], plan.Brought.Select(b => b.Id));
  }

  [Fact]
  public void What_the_set_relies_on_is_listed_once_with_everything_that_relies_on_it()
  {
    var sheet = new ImportNeed("Sheet1", "a sheet's code name");
    var plan = Plan(new() { ["A"] = ["B"] }, ["A"], new() { ["A"] = [sheet], ["B"] = [sheet, new ImportNeed("Sheet2", "a sheet's code name")] });

    Assert.Equal(["Sheet1", "Sheet2"], plan.Needs.Select(n => n.Need.What));
    Assert.Equal(["A", "B"], plan.Needs[0].UsedBy);
  }

  [Fact]
  public void What_may_be_wanted_too_is_offered_unless_its_already_coming()
  {
    var plan = Plan(new() { ["FromDialog"] = ["IPath"] }, ["FromDialog"], offers: new() { ["IPath"] = ["FromDialog", "FromString"] });

    var offer = Assert.Single(plan.Offers);
    Assert.Equal("FromString", offer.Id);
    Assert.Equal("IPath", offer.For);
  }
}
