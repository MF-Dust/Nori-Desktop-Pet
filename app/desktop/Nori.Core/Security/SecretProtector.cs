using System.Security.Cryptography;
using System.Text;

namespace Nori.Core.Security;

/// <summary>
/// 可移植的本机静态加密
///
/// 替代直接调用 DPAPI (仅 Windows): 统一用 AES-256-GCM 加密, 密钥本身交给
/// 各平台的密钥库保管 (见 ISecretKeyStore)。
///
/// 存储格式: nsec1:&lt;base64(nonce(12) | ciphertext | tag(16))&gt;
///
/// 安全边界 (务必写进文档): 本机静态加密只防「nori.db 被单独拷走」,
/// 不防同一用户下的其他进程 —— 那种场景下攻击者本来就能读到密钥文件。
/// </summary>
public static class SecretProtector
{
	/// <summary>新格式前缀</summary>
	public const string Prefix = "nsec1:";

	/// <summary>旧 DPAPI 格式前缀 (只读兼容)</summary>
	public const string LegacyDpapiPrefix = "enc:dpapi:";

	private const int NonceSize = 12;
	private const int TagSize = 16;

	/// <summary>是否为本模块加密过的值</summary>
	public static bool IsProtected(string stored) => stored.StartsWith(Prefix, StringComparison.Ordinal);

	/// <summary>是否为旧 DPAPI 格式</summary>
	public static bool IsLegacyDpapi(string stored) => stored.StartsWith(LegacyDpapiPrefix, StringComparison.Ordinal);

	/// <summary>
	/// 加密明文
	/// </summary>
	/// <param name="key">32 字节密钥</param>
	public static string Protect(ReadOnlySpan<byte> key, string plainText)
	{
		if (plainText.Length == 0) return plainText;
		byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
		byte[] plain = Encoding.UTF8.GetBytes(plainText);
		byte[] cipher = new byte[plain.Length];
		byte[] tag = new byte[TagSize];

		using AesGcm aes = new(key, TagSize);
		aes.Encrypt(nonce, plain, cipher, tag);

		byte[] payload = new byte[NonceSize + cipher.Length + TagSize];
		nonce.CopyTo(payload.AsSpan(0, NonceSize));
		cipher.CopyTo(payload.AsSpan(NonceSize, cipher.Length));
		tag.CopyTo(payload.AsSpan(NonceSize + cipher.Length, TagSize));
		return Prefix + Convert.ToBase64String(payload);
	}

	/// <summary>
	/// 解密; 格式不符、密钥不对或数据被篡改时返回 false
	/// </summary>
	public static bool TryUnprotect(ReadOnlySpan<byte> key, string stored, out string plainText)
	{
		plainText = "";
		if (!IsProtected(stored)) return false;
		try
		{
			byte[] payload = Convert.FromBase64String(stored[Prefix.Length..]);
			if (payload.Length < NonceSize + TagSize) return false;

			int cipherLength = payload.Length - NonceSize - TagSize;
			ReadOnlySpan<byte> nonce = payload.AsSpan(0, NonceSize);
			ReadOnlySpan<byte> cipher = payload.AsSpan(NonceSize, cipherLength);
			ReadOnlySpan<byte> tag = payload.AsSpan(NonceSize + cipherLength, TagSize);

			byte[] plain = new byte[cipherLength];
			using AesGcm aes = new(key, TagSize);
			aes.Decrypt(nonce, cipher, tag, plain);
			plainText = Encoding.UTF8.GetString(plain);
			return true;
		}
		catch (Exception exception) when (exception is CryptographicException or FormatException or ArgumentException)
		{
			return false;
		}
	}
}
