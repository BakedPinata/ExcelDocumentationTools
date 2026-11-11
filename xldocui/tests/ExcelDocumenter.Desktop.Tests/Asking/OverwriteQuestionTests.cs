using ExcelDocumenter.Desktop.Asking;
using ExcelDocumenter.Shared.Files;

namespace ExcelDocumenter.Desktop.Tests.Asking;

public sealed class OverwriteQuestionTests
{
  [Fact]
  public void The_dialog_offers_every_answer_the_console_does_once()
  {
    Assert.Equal(Enum.GetValues<OverwriteAnswer>().Order(), OverwriteQuestion.Answers.Select(a => a.Answer).Order());
  }

  [Fact]
  public void Closing_the_dialog_quits_as_end_of_input_does_at_the_console()
  {
    Assert.Equal(OverwriteAnswer.Quit, OverwriteQuestion.Closed);
  }

  [Fact]
  public void The_question_names_the_file_and_its_folder()
  {
    Assert.Equal("Sales-vba.html is already there", OverwriteQuestion.Heading(@"C:\Work\VBAProject\Sales-vba.html"));
    Assert.StartsWith(@"It's in C:\Work\VBAProject, and this run would change it.", OverwriteQuestion.Text(@"C:\Work\VBAProject\Sales-vba.html"), StringComparison.Ordinal);
  }
}
