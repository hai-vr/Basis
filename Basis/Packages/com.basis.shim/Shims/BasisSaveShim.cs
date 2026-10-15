using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Basis.Scripts.BasisSdk;
using Cilbox;
using UnityEngine;

namespace Basis.Shims
{
	public static class BasisSaveShim
	{
		public const int MaxSlotBytes = 256 * 1024, MaxSlots = 32, MaxStoreBytes = 1024 * 1024, MaxNameLength = 128;
		const int HeaderSize = 16, MaxStores = 128, MaxStoresPerBox = 4, MaxWritesPerSecond = 8;
		const uint Magic = (uint)('B' | 'S' << 8 | 'A' << 16 | 'V' << 24);
		const byte Version = 1;
		const string RootFolder = "CilboxSaves", Extension = ".bsave";
		public static bool Save(object self, string slot, byte[] data) => Save(self, null, slot, data);
		public static byte[] Load(object self, string slot) => Load(self, null, slot);
		public static bool Delete(object self, string slot) => Delete(self, null, slot);
		public static bool Save(object self, string store, string slot, byte[] data)
		{
			CilboxBasisCommon box = Caller(self, nameof(Save));
			string root = Root(), folder = box == null || data == null || data.Length > MaxSlotBytes ? null : StoreFolder(box, root, store), file = SlotFile(folder, slot);
			if (file == null || !Claim(box, folder) || !Spend(box)) return false;
			string temp = Path.ChangeExtension(file, ".tmp");
			try
			{
				long total = HeaderSize + data.Length;
				int slots = 1;
				if (Directory.Exists(folder))
				{
					foreach (string other in Directory.GetFiles(folder, "*" + Extension))
					{
						if (string.Equals(Path.GetFileName(other), Path.GetFileName(file), StringComparison.OrdinalIgnoreCase)) continue;
						total += new FileInfo(other).Length;
						slots++;
					}
				}
				else if (Directory.Exists(root) && Directory.GetDirectories(root).Length >= MaxStores) return false;
				if (slots > MaxSlots || total > MaxStoreBytes) return false;
				byte[] bytes = new byte[HeaderSize + data.Length];
				BinaryPrimitives.WriteUInt32LittleEndian(new Span<byte>(bytes, 0, 4), Magic);
				bytes[4] = Version;
				BinaryPrimitives.WriteInt32LittleEndian(new Span<byte>(bytes, 8, 4), data.Length);
				BinaryPrimitives.WriteUInt32LittleEndian(new Span<byte>(bytes, 12, 4), Checksum(data));
				Buffer.BlockCopy(data, 0, bytes, HeaderSize, data.Length);
				Directory.CreateDirectory(folder);
				File.WriteAllBytes(temp, bytes);
				if (File.Exists(file)) File.Replace(temp, file, null);
				else File.Move(temp, file);
				return true;
			}
			catch (Exception)
			{
				try { File.Delete(temp); } catch (Exception) { }
				return false;
			}
		}
		public static byte[] Load(object self, string store, string slot)
		{
			CilboxBasisCommon box = Caller(self, nameof(Load));
			string folder = box == null ? null : StoreFolder(box, Root(), store), file = SlotFile(folder, slot);
			if (file == null || !Claim(box, folder)) return null;
			try
			{
				if (!File.Exists(file)) return null;
				using (FileStream stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
				{
					long length = stream.Length;
					if (length < HeaderSize || length > HeaderSize + MaxSlotBytes) return null;
					byte[] header = new byte[HeaderSize], data = new byte[length - HeaderSize];
					if (!Fill(stream, header) || !Fill(stream, data)) return null;
					ReadOnlySpan<byte> fields = header;
					if (BinaryPrimitives.ReadUInt32LittleEndian(fields) != Magic || header[4] != Version || header[5] != 0 || header[6] != 0 || header[7] != 0) return null;
					if (BinaryPrimitives.ReadInt32LittleEndian(fields.Slice(8)) != data.Length || BinaryPrimitives.ReadUInt32LittleEndian(fields.Slice(12)) != Checksum(data)) return null;
					return data;
				}
			}
			catch (Exception) { return null; }
		}
		public static bool Delete(object self, string store, string slot)
		{
			CilboxBasisCommon box = Caller(self, nameof(Delete));
			string folder = box == null ? null : StoreFolder(box, Root(), store), file = SlotFile(folder, slot);
			if (file == null || !Claim(box, folder) || !Spend(box)) return false;
			try
			{
				if (!File.Exists(file)) return false;
				File.Delete(file);
				if (Directory.GetFileSystemEntries(folder).Length == 0) Directory.Delete(folder);
				return true;
			}
			catch (Exception) { return false; }
		}
		static CilboxBasisCommon Caller(object self, string method)
		{
			if (!(self is CilboxProxy proxy) || proxy == null || !(proxy.box is CilboxBasisCommon box) || box == null || box.interpreterAccountingDepth == 0) return null;
			return CilboxBasisConsent.Permits(box, CilboxBasisCommon.MethodItem(typeof(BasisSaveShim), method)) ? box : null;
		}
		static string Root() => Path.Combine(Application.persistentDataPath, RootFolder);
		static string StoreFolder(CilboxBasisCommon box, string root, string store)
		{
			string key = store != null ? store.Trim() : ContentKey(box);
			return string.IsNullOrEmpty(key) || key.Length > MaxNameLength ? null : Path.Combine(root, Hash(key.ToLowerInvariant()));
		}
		static string SlotFile(string folder, string slot) => folder == null || string.IsNullOrEmpty(slot) || slot.Length > MaxNameLength ? null : Path.Combine(folder, Hash(slot) + Extension);
		static string ContentKey(CilboxBasisCommon box)
		{
			if (box.saveContentKey != null) return box.saveContentKey;
			BasisContentBase content = box.GetComponentInParent<BasisContentBase>(true);
			if (content == null && box.gameObject.scene.IsValid())
			{
				foreach (GameObject root in box.gameObject.scene.GetRootGameObjects())
				{
					content = root.GetComponentInChildren<BasisScene>(true);
					if (content != null) break;
				}
			}
			string id = content != null && content.BasisBundleDescription != null ? content.BasisBundleDescription.ContentGroupId : null;
			box.saveContentKey = string.IsNullOrWhiteSpace(id) ? box.consentHash ?? string.Empty : id.Trim();
			return box.saveContentKey;
		}
		static bool Claim(CilboxBasisCommon box, string folder)
		{
			box.saveStores ??= new HashSet<string>(StringComparer.Ordinal);
			return box.saveStores.Contains(folder) || (box.saveStores.Count < MaxStoresPerBox && box.saveStores.Add(folder));
		}
		static bool Spend(CilboxBasisCommon box)
		{
			double now = Time.realtimeSinceStartupAsDouble;
			if (now - box.saveWindowStart >= 1d || now < box.saveWindowStart)
			{
				box.saveWindowStart = now;
				box.saveWindowWrites = 0;
			}
			return ++box.saveWindowWrites <= MaxWritesPerSecond;
		}
		static bool Fill(Stream stream, byte[] buffer)
		{
			for (int read = 0, got; read < buffer.Length; read += got)
			{
				if ((got = stream.Read(buffer, read, buffer.Length - read)) <= 0) return false;
			}
			return true;
		}
		static string Hash(string text)
		{
			using (SHA256 sha = SHA256.Create())
			{
				byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
				StringBuilder hex = new StringBuilder(32);
				for (int i = 0; i < 16; i++) hex.Append(digest[i].ToString("x2"));
				return hex.ToString();
			}
		}
		static uint Checksum(byte[] data)
		{
			uint hash = 2166136261;
			foreach (byte value in data) hash = (hash ^ value) * 16777619;
			return hash;
		}
	}
}
