using System.Text;
using PowerQueryDocumenter.Export;

namespace PowerQueryDocumenter.Tests.Export;

public sealed class PlantUmlEncoderTests
{
  // Expected values computed independently from the reference algorithm at https://plantuml.com/text-encoding.
  [Theory]
  [InlineData("abc", "OM9Z")]
  [InlineData("ab", "OM80")]
  [InlineData("a", "OG00")]
  [InlineData("", "")]
  public void Encode64_matches_the_reference_algorithm(string input, string expected)
  {
    Assert.Equal(expected, PlantUmlEncoder.Encode64(Encoding.ASCII.GetBytes(input)));
  }

  [Fact]
  public void Encode64_uses_the_full_PlantUML_alphabet_at_both_ends()
  {
    Assert.Equal("0000", PlantUmlEncoder.Encode64(new byte[] { 0, 0, 0 }));
    Assert.Equal("____", PlantUmlEncoder.Encode64(new byte[] { 255, 255, 255 }));
  }

  [Fact]
  public void Decodes_the_example_from_the_PlantUML_documentation()
  {
    Assert.Equal("Bob -> Alice : hello", PlantUmlEncoder.Decode("SyfFKj2rKt3CoKnELR1Io4ZDoSa70000"));
  }

  [Fact]
  public void Decodes_text_encoded_by_the_plantuml_encoder_npm_package()
  {
    // Produced by plantuml-encoder 1.4.0 (JavaScript), including a non-ASCII character.
    const string encoded = "SoWkIImgAStDuIfAJIv9p4lFILLG2axCIIrM23gyCZqpFqz98R5OeT2Ggz1GGLVNJg7GYCiXDIy5f0C0";

    Assert.Equal("@startuml\nrectangle \"Sales Région\" as q1\nq1 --> q2\n@enduml\n", PlantUmlEncoder.Decode(encoded));
  }

  [Theory]
  [InlineData("@startuml\nA --> B\n@enduml\n")]
  [InlineData("@startuml\nrectangle \"Ünïcödé 🚀 &#34;\" as q1\n@enduml\n")]
  [InlineData("")]
  public void Encode_and_decode_round_trip(string diagram)
  {
    Assert.Equal(diagram, PlantUmlEncoder.Decode(PlantUmlEncoder.Encode(diagram)));
  }

  [Fact]
  public void Encoded_text_only_uses_URL_safe_characters()
  {
    var encoded = PlantUmlEncoder.Encode(string.Concat(Enumerable.Range(0, 500).Select(i => $"q{i} --> q{i + 1}\n")));

    Assert.Matches("^[0-9A-Za-z_-]+$", encoded);
  }

  [Fact]
  public void Decoding_an_invalid_character_throws_FormatException()
  {
    Assert.Throws<FormatException>(() => PlantUmlEncoder.Decode64("ab+c"));
  }
}
