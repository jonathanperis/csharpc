// The symmetric ciphers of System.Security.Cryptography as the framework's IL serves them over the OpenSSL contract
// (docs/il-first/System.Security.Cryptography.md): AES in CBC, ECB and CFB with every padding mode, through the one-shot
// APIs, transforms and CryptoStream; TripleDES; the AEAD ciphers AesGcm, AesCcm and ChaCha20Poly1305; AES key wrap; and
// the argument, padding and authentication errors their callers see.
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

class Program
{
    static string Hex(ReadOnlySpan<byte> b) => Convert.ToHexString(b);

    static void Try(string what, Action a)
    {
        try { a(); Console.WriteLine(what + ": ok"); }
        catch (Exception e) { Console.WriteLine(what + ": " + e.GetType().Name + ": " + e.Message); }
    }

    static void Main()
    {
        byte[] key = new byte[32], iv = new byte[16], nonce = new byte[12];
        for (int i = 0; i < key.Length; i++) key[i] = (byte)(i * 7 + 1);
        for (int i = 0; i < iv.Length; i++) iv[i] = (byte)(255 - i);
        for (int i = 0; i < nonce.Length; i++) nonce[i] = (byte)(i + 100);
        byte[] text = Encoding.ASCII.GetBytes("Thirty-three bytes of plain text!");

        using (var aes = Aes.Create())
        {
            aes.Key = key;
            Console.WriteLine("Aes " + aes.KeySize + " " + aes.BlockSize + " " + aes.Mode + " " + aes.Padding + " " + aes.FeedbackSize);
            foreach (var padding in new[] { PaddingMode.PKCS7, PaddingMode.ISO10126, PaddingMode.ANSIX923, PaddingMode.Zeros })
            {
                byte[] ct = aes.EncryptCbc(text, iv, padding);
                byte[] pt = aes.DecryptCbc(ct, iv, padding);
                // ISO 10126 pads with random bytes: print only what is fixed.
                Console.WriteLine("CBC " + padding + " " + ct.Length + " " + (padding == PaddingMode.ISO10126 ? Hex(ct[..32]) : Hex(ct)) + " " + Encoding.ASCII.GetString(pt).TrimEnd('\0'));
            }
            Console.WriteLine("ECB " + Hex(aes.EncryptEcb(text, PaddingMode.PKCS7)));
            Console.WriteLine("CFB8 " + Hex(aes.EncryptCfb(text, iv, PaddingMode.None, 8)));
            Console.WriteLine("CFB128 " + Hex(aes.EncryptCfb(text, iv, PaddingMode.PKCS7, 128)));

            aes.IV = iv;
            using (var enc = aes.CreateEncryptor())
            {
                Console.WriteLine("transform " + enc.InputBlockSize + " " + enc.OutputBlockSize + " " + enc.CanTransformMultipleBlocks + " " + enc.CanReuseTransform);
                byte[] buffer = new byte[64];
                int n = enc.TransformBlock(text, 0, 32, buffer, 0);
                byte[] last = enc.TransformFinalBlock(text, 32, 1);
                Console.WriteLine("blocks " + n + " " + Hex(buffer.AsSpan(0, n)) + " " + Hex(last));
            }
            using (var ms = new MemoryStream())
            {
                using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write, leaveOpen: true))
                {
                    cs.Write(text, 0, 5);
                    cs.Write(text, 5, text.Length - 5);
                }
                byte[] ct = ms.ToArray();
                Console.WriteLine("CryptoStream " + Hex(ct));
                using var back = new CryptoStream(new MemoryStream(ct), aes.CreateDecryptor(), CryptoStreamMode.Read);
                using var reader = new StreamReader(back);
                Console.WriteLine("read back " + reader.ReadToEnd());
            }
            Try("bad padding", () => aes.DecryptCbc(new byte[32], iv));
            Try("partial block", () => aes.EncryptCbc(text, iv, PaddingMode.None));
            Try("bad key size", () => aes.Key = new byte[7]);
            Try("bad IV size", () => aes.EncryptCbc(text, new byte[8]));
        }

        using (var des3 = TripleDES.Create())
        {
            des3.Key = key[..24];
            Console.WriteLine("TripleDES " + des3.KeySize + " " + Hex(des3.EncryptCbc(text, iv[..8])));
        }

        Console.WriteLine("AEAD supported " + AesGcm.IsSupported + " " + AesCcm.IsSupported + " " + ChaCha20Poly1305.IsSupported);
        byte[] aad = Encoding.ASCII.GetBytes("header");
        using (var gcm = new AesGcm(key, 16))
        {
            byte[] ct = new byte[text.Length], tag = new byte[16], pt = new byte[text.Length];
            gcm.Encrypt(nonce, text, ct, tag, aad);
            Console.WriteLine("GCM " + Hex(ct) + " " + Hex(tag) + " " + gcm.TagSizeInBytes);
            gcm.Decrypt(nonce, ct, tag, pt, aad);
            Console.WriteLine("GCM back " + Encoding.ASCII.GetString(pt));
            tag[0] ^= 1;
            Try("GCM tag", () => gcm.Decrypt(nonce, ct, tag, pt, aad));
            Try("GCM nonce", () => gcm.Encrypt(new byte[5], text, ct, tag));
        }
        using (var ccm = new AesCcm(key[..16]))
        {
            byte[] ct = new byte[text.Length], tag = new byte[12];
            ccm.Encrypt(nonce[..11], text, ct, tag, aad);
            Console.WriteLine("CCM " + Hex(ct) + " " + Hex(tag));
        }
        using (var chacha = new ChaCha20Poly1305(key))
        {
            byte[] ct = new byte[text.Length], tag = new byte[16];
            chacha.Encrypt(nonce, text, ct, tag, aad);
            Console.WriteLine("ChaCha20Poly1305 " + Hex(ct) + " " + Hex(tag));
        }

        using (var aes = Aes.Create())
        {
            aes.Key = key[..16];
            byte[] wrapped = aes.EncryptKeyWrapPadded(key[..20]);
            Console.WriteLine("KeyWrapPadded " + Hex(wrapped) + " " + Hex(aes.DecryptKeyWrapPadded(wrapped)));
        }
        Try("AesGcm key", () => new AesGcm(new byte[10], 16));
    }
}
