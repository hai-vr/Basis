#nullable enable

using System;

namespace Basis.Contrib.Crypto
{
	/// ChaCha20-Poly1305 AEAD bound to a single 32-byte key, reusable across many
	/// packets. Uses the hardware-accelerated <see cref="System.Security.Cryptography.ChaCha20Poly1305"/>
	/// when the runtime supports it (Linux/OpenSSL, modern Windows) and falls back to
	/// the managed BouncyCastle implementation otherwise (Unity / netstandard2.1).
	///
	/// Seal/Open operate in place on a caller-owned buffer; the 16-byte tag is written
	/// to / read from a caller-supplied location. Instances are safe for concurrent use.
	public sealed class BasisAeadCipher : IDisposable
	{
		public const int KeySize = 32;
		public const int NonceSize = 12;
		public const int TagSize = 16;

		private readonly object _sync = new object();
		private byte[] _scratchIn = Array.Empty<byte>();
		private byte[] _scratchOut = Array.Empty<byte>();
		private readonly byte[] _nonceBuf = new byte[NonceSize];
		private readonly byte[] _aadBuf = new byte[1];

#if NETSTANDARD2_1
		private readonly Org.BouncyCastle.Crypto.Modes.ChaCha20Poly1305 _bc;
		private readonly Org.BouncyCastle.Crypto.Parameters.KeyParameter _key;
#else
		private readonly System.Security.Cryptography.ChaCha20Poly1305? _native;
		private readonly Org.BouncyCastle.Crypto.Modes.ChaCha20Poly1305? _bc;
		private readonly Org.BouncyCastle.Crypto.Parameters.KeyParameter? _key;
#endif

		public BasisAeadCipher(ReadOnlySpan<byte> key)
		{
			if (key.Length != KeySize)
				throw new ArgumentException($"Key must be {KeySize} bytes", nameof(key));
#if NETSTANDARD2_1
			_bc = new Org.BouncyCastle.Crypto.Modes.ChaCha20Poly1305();
			_key = new Org.BouncyCastle.Crypto.Parameters.KeyParameter(key.ToArray());
#else
			if (System.Security.Cryptography.ChaCha20Poly1305.IsSupported)
			{
				_native = new System.Security.Cryptography.ChaCha20Poly1305(key);
				_bc = null;
				_key = null;
			}
			else
			{
				_native = null;
				_bc = new Org.BouncyCastle.Crypto.Modes.ChaCha20Poly1305();
				_key = new Org.BouncyCastle.Crypto.Parameters.KeyParameter(key.ToArray());
			}
#endif
		}

		/// Encrypts <paramref name="length"/> bytes at <paramref name="buffer"/>[<paramref name="offset"/>]
		/// in place and writes the authentication tag to <paramref name="tagDest"/>[<paramref name="tagOffset"/>].
		public void Seal(ReadOnlySpan<byte> nonce, byte aad, byte[] buffer, int offset, int length, byte[] tagDest, int tagOffset)
		{
			// ChaCha20Poly1305/AesGcm reuse one native cipher context per instance and are
			// not safe for the concurrent sends a busy server makes — serialize all calls.
			lock (_sync)
			{
#if !NETSTANDARD2_1
				if (_native != null)
				{
					Span<byte> aadSpan = stackalloc byte[1];
					aadSpan[0] = aad;
					var region = new Span<byte>(buffer, offset, length);
					_native.Encrypt(nonce, region, region, new Span<byte>(tagDest, tagOffset, TagSize), aadSpan);
					return;
				}
#endif
				nonce.CopyTo(_nonceBuf);
				_aadBuf[0] = aad;
				_bc!.Init(true, new Org.BouncyCastle.Crypto.Parameters.AeadParameters(_key, TagSize * 8, _nonceBuf, _aadBuf));
				EnsureScratchOut(_bc.GetOutputSize(length));
				int produced = _bc.ProcessBytes(buffer, offset, length, _scratchOut, 0);
				produced += _bc.DoFinal(_scratchOut, produced);
				Buffer.BlockCopy(_scratchOut, 0, buffer, offset, length);
				Buffer.BlockCopy(_scratchOut, length, tagDest, tagOffset, TagSize);
			}
		}

		/// Decrypts <paramref name="length"/> bytes at <paramref name="buffer"/>[<paramref name="offset"/>]
		/// in place, verifying against the tag at <paramref name="tag"/>[<paramref name="tagOffset"/>].
		/// Returns false (and leaves the buffer undefined) on tag mismatch.
		public bool Open(ReadOnlySpan<byte> nonce, byte aad, byte[] buffer, int offset, int length, byte[] tag, int tagOffset)
		{
			// Locked for the same reason as Seal. Any failure (tag mismatch or a malformed
			// attacker packet) returns false rather than throwing into the receive loop.
			lock (_sync)
			{
				try
				{
#if !NETSTANDARD2_1
					if (_native != null)
					{
						Span<byte> aadSpan = stackalloc byte[1];
						aadSpan[0] = aad;
						var region = new Span<byte>(buffer, offset, length);
						_native.Decrypt(nonce, region, new ReadOnlySpan<byte>(tag, tagOffset, TagSize), region, aadSpan);
						return true;
					}
#endif
					nonce.CopyTo(_nonceBuf);
					_aadBuf[0] = aad;
					_bc!.Init(false, new Org.BouncyCastle.Crypto.Parameters.AeadParameters(_key, TagSize * 8, _nonceBuf, _aadBuf));
					int inLen = length + TagSize;
					EnsureScratchIn(inLen);
					EnsureScratchOut(_bc.GetOutputSize(inLen));
					Buffer.BlockCopy(buffer, offset, _scratchIn, 0, length);
					Buffer.BlockCopy(tag, tagOffset, _scratchIn, length, TagSize);
					int produced = _bc.ProcessBytes(_scratchIn, 0, inLen, _scratchOut, 0);
					produced += _bc.DoFinal(_scratchOut, produced);
					Buffer.BlockCopy(_scratchOut, 0, buffer, offset, length);
					return true;
				}
				catch
				{
					return false;
				}
			}
		}

		private void EnsureScratchIn(int size)
		{
			if (_scratchIn.Length < size) _scratchIn = new byte[size];
		}

		private void EnsureScratchOut(int size)
		{
			if (_scratchOut.Length < size) _scratchOut = new byte[size];
		}

		public void Dispose()
		{
#if !NETSTANDARD2_1
			_native?.Dispose();
#endif
		}
	}
}
