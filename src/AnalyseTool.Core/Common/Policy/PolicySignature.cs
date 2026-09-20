using System.Security.Cryptography;
using System.Text;

namespace AnalyseTool.Core.Common.Policy
{
    /// <summary>What a verification found.</summary>
    internal enum SignatureState
    {
        /// <summary>No key is known, so nothing was checked (v1 without a key relies on HTTPS + preview).</summary>
        NoKey,
        Verified,
        /// <summary>A key is known but no <c>.sig</c> exists next to the policy.</summary>
        Missing,
        Invalid,
    }

    /// <summary>
    /// Detached policy signatures (design §3c): ECDSA P-256 over the exact bytes of <c>policy.json</c>,
    /// stored base64 in <c>policy.json.sig</c> next to it. The public key travels out of band — the
    /// machine pointer, the invite link, or typed once at Join — as base64 SubjectPublicKeyInfo, and
    /// its fingerprint is what a coordinator reads out to staff.
    /// </summary>
    internal static class PolicySignature
    {
        public static SignatureState Verify(string? publicKeyBase64, byte[] policyBytes, string? signatureBase64)
        {
            if (string.IsNullOrWhiteSpace(publicKeyBase64)) return SignatureState.NoKey;
            if (string.IsNullOrWhiteSpace(signatureBase64)) return SignatureState.Missing;
            try
            {
                using ECDsa key = ECDsa.Create();
                key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyBase64.Trim()), out _);
                byte[] signature = Convert.FromBase64String(signatureBase64.Trim());
                return key.VerifyData(policyBytes, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence)
                    ? SignatureState.Verified
                    : SignatureState.Invalid;
            }
            catch
            {
                return SignatureState.Invalid;
            }
        }

        /// <summary>Signs with a PKCS#8 private key (base64). Used by the CLI's <c>policy sign</c> and the tests.</summary>
        public static string Sign(string privateKeyBase64, byte[] policyBytes)
        {
            using ECDsa key = ECDsa.Create();
            key.ImportPkcs8PrivateKey(Convert.FromBase64String(privateKeyBase64.Trim()), out _);
            return Convert.ToBase64String(key.SignData(policyBytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));
        }

        /// <summary>A fresh P-256 key pair: (public SPKI base64, private PKCS#8 base64).</summary>
        public static (string PublicKey, string PrivateKey) GenerateKeyPair()
        {
            using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            return (Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()),
                    Convert.ToBase64String(key.ExportPkcs8PrivateKey()));
        }

        /// <summary>What a coordinator reads out and a user compares: the first 16 hex characters of
        /// SHA-256 over the public key, in groups of four.</summary>
        public static string? Fingerprint(string? publicKeyBase64)
        {
            if (string.IsNullOrWhiteSpace(publicKeyBase64)) return null;
            try
            {
                string hex = Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(publicKeyBase64.Trim())))[..16];
                return string.Join(" ", Enumerable.Range(0, 4).Select(i => hex.Substring(i * 4, 4)));
            }
            catch { return null; }
        }

        /// <summary>Where the signature of a policy lives: next to it, same name plus <c>.sig</c>.</summary>
        public static string SignaturePathFor(string policyLocation) => policyLocation + ".sig";

        public static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);
    }
}
