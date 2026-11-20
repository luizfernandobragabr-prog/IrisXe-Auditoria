using System.Text;

namespace IrisXe.Thermal.Core.Decoding;

/// <summary>
/// Utilitarios de manipulacao de bits para valores brutos de MSR de 64 bits.
/// Nenhuma interpretacao aqui e presumida: toda mascara usada pelos decodificadores
/// cita a documentacao Intel aplicavel ao Tiger Lake.
/// </summary>
public static class Bits
{
    /// <summary>Extrai <paramref name="width"/> bits a partir de <paramref name="loBit"/>.</summary>
    public static ulong Extract(ulong value, int loBit, int width)
        => width >= 64 ? value : (value >> loBit) & ((1UL << width) - 1UL);

    public static string Hex16(ulong value) => $"0x{value:X16}";

    /// <summary>Representacao binaria de 64 bits em grupos de 8, do bit 63 ao bit 0.</summary>
    public static string Bin64(ulong value)
    {
        var sb = new StringBuilder(71);
        for (var bit = 63; bit >= 0; bit--)
        {
            sb.Append(((value >> bit) & 1UL) == 1UL ? '1' : '0');
            if (bit > 0 && bit % 8 == 0)
            {
                sb.Append(' ');
            }
        }

        return sb.ToString();
    }

    public static string Range(int hi, int lo) => hi == lo ? $"[{hi}]" : $"[{hi}:{lo}]";
}
