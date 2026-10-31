package dev.usage.monitor

import org.json.JSONObject
import java.security.SecureRandom
import javax.crypto.Cipher
import javax.crypto.Mac
import javax.crypto.spec.GCMParameterSpec
import javax.crypto.spec.SecretKeySpec

/** Mirrors the PC side (Usage.Core.Link.Pairing). The relay only ever sees the room id and opaque ciphertext. */
class Pairing private constructor(val code: String, val relayUrl: String, val pcName: String) {
    val keys: Keys

    class Keys(val roomId: String, val key: ByteArray)

    init {
        val secret = fromCode(code) ?: throw IllegalArgumentException("bad code")
        keys = Keys(hkdf(secret, "usage/v1/room", 16).toHex(), hkdf(secret, "usage/v1/key", 32))
    }

    fun seal(role: Char, message: JSONObject): String {
        val nonce = ByteArray(12).also { SecureRandom().nextBytes(it) }
        val c = Cipher.getInstance("AES/GCM/NoPadding")
        c.init(Cipher.ENCRYPT_MODE, SecretKeySpec(keys.key, "AES"), GCMParameterSpec(128, nonce))
        c.updateAAD("${keys.roomId}:$role".toByteArray())
        val ct = c.doFinal(message.toString().toByteArray())
        return JSONObject().put("v", 1).put("n", b64(nonce)).put("c", b64(ct)).toString()
    }

    /** Returns null for anything that is not authentic, fresh and sent by [fromRole]. */
    fun open(fromRole: Char, frame: String, nowMs: Long = System.currentTimeMillis()): JSONObject? = try {
        val env = JSONObject(frame)
        if (env.optInt("v") != 1) null else {
            val c = Cipher.getInstance("AES/GCM/NoPadding")
            c.init(Cipher.DECRYPT_MODE, SecretKeySpec(keys.key, "AES"), GCMParameterSpec(128, unb64(env.getString("n"))))
            c.updateAAD("${keys.roomId}:$fromRole".toByteArray())
            val msg = JSONObject(String(c.doFinal(unb64(env.getString("c")))))
            val ts = msg.optLong("ts", 0)
            if (Math.abs(nowMs - ts) > 10 * 60 * 1000) null else msg
        }
    } catch (_: Exception) { null }

    companion object {
        private const val ALPHA = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"
        const val DEFAULT_RELAY = "wss://usage-relay.neves-lab.workers.dev"

        fun create(code: String, relayUrl: String?, pcName: String?): Pairing? = try {
            Pairing(normalize(code), relayUrl?.takeIf { it.startsWith("wss://") || it.startsWith("ws://") } ?: DEFAULT_RELAY, pcName?.takeIf { it.isNotBlank() } ?: "PC")
        } catch (_: Exception) { null }

        /** Accepts a raw code (any case, spaces/dashes) or a usage://pair?k=... link. */
        fun parse(input: String): Pairing? {
            val text = input.trim()
            if (text.startsWith("usage://", true)) {
                val q = text.substringAfter("?", "").split("&").mapNotNull { p -> p.split("=", limit = 2).takeIf { it.size == 2 }?.let { it[0] to java.net.URLDecoder.decode(it[1], "UTF-8") } }.toMap()
                return create(q["k"] ?: return null, q["r"], q["n"])
            }
            return create(text, null, null)
        }

        fun normalize(code: String) = code.uppercase().filter { it in ALPHA }

        fun fromCode(code: String): ByteArray? {
            val clean = normalize(code)
            if (clean.length != 26) return null
            val out = ArrayList<Byte>(); var buf = 0; var bits = 0
            for (ch in clean) {
                buf = (buf shl 5) or ALPHA.indexOf(ch); bits += 5
                if (bits >= 8) { out.add(((buf shr (bits - 8)) and 0xFF).toByte()); bits -= 8; buf = buf and ((1 shl bits) - 1) }
            }
            return if (out.size == 16) out.toByteArray() else null
        }

        /** HKDF-SHA256 with empty salt (RFC 5869), identical to .NET HKDF.DeriveKey. */
        fun hkdf(ikm: ByteArray, info: String, len: Int): ByteArray {
            val hmac = { key: ByteArray, data: ByteArray -> Mac.getInstance("HmacSHA256").apply { init(SecretKeySpec(key, "HmacSHA256")) }.doFinal(data) }
            val prk = hmac(ByteArray(32), ikm)
            var t = ByteArray(0); val out = java.io.ByteArrayOutputStream(); var i = 1
            while (out.size() < len) { t = hmac(prk, t + info.toByteArray() + byteArrayOf(i.toByte())); out.write(t); i++ }
            return out.toByteArray().copyOf(len)
        }

        private fun b64(b: ByteArray) = java.util.Base64.getEncoder().encodeToString(b)
        private fun unb64(s: String) = java.util.Base64.getDecoder().decode(s)
        private fun ByteArray.toHex() = joinToString("") { "%02x".format(it) }
    }
}
