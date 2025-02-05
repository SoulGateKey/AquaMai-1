namespace AquaMai.ErrorReport;

public class ChecksumCalculator
{
    private static readonly uint[] CrcTable = new uint[256];

    static ChecksumCalculator()
    {
        for (uint i = 0; i < 256; i++)
        {
            uint crc = i;
            for (int j = 0; j < 8; j++)
            {
                crc = (crc & 1) != 0 ? (0xEDB88320 ^ (crc >> 1)) : (crc >> 1);
            }
            CrcTable[i] = crc;
        }
    }

    public static uint GetChecksum(byte[] data, uint k)
    {
        uint crc = 0xFFFFFFFF; // Initial value
        foreach (byte b in data)
        {
            crc = (crc >> 8) ^ CrcTable[(crc ^ b) & 0xFF];
        }
        return (crc ^ k ^ 0xFFFFFFFF); // Final XOR value and ensure 32-bit unsigned
    }
}