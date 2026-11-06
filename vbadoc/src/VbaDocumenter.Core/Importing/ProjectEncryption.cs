using System.Buffers.Binary;
using System.Text;

namespace VbaDocumenter.Core.Importing;

/// <summary>
/// The "encryption" of the CMG, DPB and GC values in a project's PROJECT stream (MS-OVBA 2.4.3), which say whether it's
/// protected, its password and whether it's visible. A new project needs all three, saying it's none of these.
/// </summary>
public static class ProjectEncryption
{
  const byte Version = 2;

  /// <summary>Encrypts the data, as the hex the PROJECT stream holds (2.4.3.2).</summary>
  public static string Encrypt(byte[] data, byte projectKey, byte seed)
  {
    ArgumentNullException.ThrowIfNull(data);
    var output = new List<byte>();
    byte versionEnc = (byte)(seed ^ Version);
    byte keyEnc = (byte)(seed ^ projectKey);
    output.Add(seed);
    output.Add(versionEnc);
    output.Add(keyEnc);

    byte unencrypted1 = projectKey, encrypted1 = keyEnc, encrypted2 = versionEnc;
    void Put(byte b)
    {
      byte enc = (byte)(b ^ (byte)(encrypted2 + unencrypted1));
      output.Add(enc);
      encrypted2 = encrypted1;
      encrypted1 = enc;
      unencrypted1 = b;
    }

    for (int i = 0; i < (seed & 6) / 2; i++) Put(0); // ignored bytes: any value
    Span<byte> length = stackalloc byte[4];
    BinaryPrimitives.WriteInt32LittleEndian(length, data.Length);
    foreach (var b in length) Put(b);
    foreach (var b in data) Put(b);
    return Convert.ToHexString(output.ToArray());
  }

  /// <summary>Reverses <see cref="Encrypt"/> (2.4.3.3), giving the key it was made with and the data.</summary>
  public static (byte Key, byte[] Data) Decrypt(string hex)
  {
    var input = Convert.FromHexString(hex);
    byte seed = input[0], versionEnc = input[1], keyEnc = input[2];
    byte key = (byte)(seed ^ keyEnc);
    byte unencrypted1 = key, encrypted1 = keyEnc, encrypted2 = versionEnc;
    int at = 3;
    byte Next()
    {
      byte enc = input[at++];
      byte b = (byte)(enc ^ (byte)(encrypted2 + unencrypted1));
      encrypted2 = encrypted1;
      encrypted1 = enc;
      unencrypted1 = b;
      return b;
    }

    for (int i = 0; i < (seed & 6) / 2; i++) Next();
    Span<byte> length = stackalloc byte[4];
    for (int i = 0; i < 4; i++) length[i] = Next();
    var data = new byte[BinaryPrimitives.ReadInt32LittleEndian(length)];
    for (int i = 0; i < data.Length; i++) data[i] = Next();
    return (key, data);
  }

  /// <summary>The project's key: the low byte of the sum of the bytes of its ID, as PROJECT writes it, braces included.</summary>
  public static byte ProjectKey(string projectId)
  {
    byte sum = 0;
    foreach (var b in Encoding.ASCII.GetBytes(projectId)) sum += b;
    return sum;
  }
}
