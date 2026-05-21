#nullable enable

using System;
using Org.BouncyCastle.Crypto.Agreement;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

namespace Basis.Contrib.Crypto
{
	/// X25519 (Curve25519 ECDH) key agreement. 32-byte keys and shared secrets.
	public static class BasisX25519
	{
		public const int KeySize = 32;
		public const int SharedSecretSize = 32;

		private static readonly SecureRandom Rng = new SecureRandom();

		public static void GenerateKeyPair(out byte[] privateKey, out byte[] publicKey)
		{
			var priv = new X25519PrivateKeyParameters(Rng);
			var pub = priv.GeneratePublicKey();
			privateKey = new byte[KeySize];
			publicKey = new byte[KeySize];
			priv.Encode(privateKey, 0);
			pub.Encode(publicKey, 0);
		}

		public static byte[] DerivePublicKey(ReadOnlySpan<byte> privateKey)
		{
			var priv = new X25519PrivateKeyParameters(privateKey.ToArray(), 0);
			var pub = priv.GeneratePublicKey();
			var encoded = new byte[KeySize];
			pub.Encode(encoded, 0);
			return encoded;
		}

		public static byte[] Agree(ReadOnlySpan<byte> privateKey, ReadOnlySpan<byte> peerPublicKey)
		{
			var priv = new X25519PrivateKeyParameters(privateKey.ToArray(), 0);
			var pub = new X25519PublicKeyParameters(peerPublicKey.ToArray(), 0);
			var agreement = new X25519Agreement();
			agreement.Init(priv);
			var secret = new byte[agreement.AgreementSize];
			agreement.CalculateAgreement(pub, secret, 0);
			return secret;
		}
	}
}
