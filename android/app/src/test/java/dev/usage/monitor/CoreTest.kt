package dev.usage.monitor

import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class CoreTest {
    private val zero = "AAAAAAAAAAAAAAAAAAAAAAAAAA"

    // Produced by an independent implementation (Node crypto): HKDF room id and a sealed host frame (ts = 1000).
    private val room = "81fe91cc8b818716aa68a95e76a2b05c"
    private val frame = "{\"v\":1,\"n\":\"BwcHBwcHBwcHBwcH\",\"c\":\"hW4ngDiHxoj3OrwTI1Loun9VAOt4gK1RlenuRNBm+wRyaSb5kWoCddjDL04SHNZTJV1V0tsrwhM3qx4ITkLLb+ht\"}"

    @Test fun deriveMatchesIndependentImplementation() {
        assertEquals(room, Pairing.create(zero, null, null)!!.keys.roomId)
    }

    @Test fun opensFrameSealedByAnotherStack() {
        val m = Pairing.create(zero, null, null)!!.open('h', frame, 1000)
        assertEquals("snapshot", m!!.getString("t"))
    }

    @Test fun rejectsWrongDirectionTamperingAndReplay() {
        val p = Pairing.create(zero, null, null)!!
        assertNull(p.open('c', frame, 1000))
        assertNull(p.open('h', frame, 1000 + 11 * 60 * 1000))
        assertNull(p.open('h', frame.replace("hW4n", "hW5n"), 1000))
        assertNull(p.open('h', "nope", 1000))
    }

    @Test fun sealOpenRoundTrip() {
        val p = Pairing.create("ZVQWY-RKJHC-65K6C-JG524-NTWH6-U", null, null)!!
        val sealed = p.seal('c', JSONObject().put("t", "get").put("ts", 5000L))
        assertEquals("get", p.open('c', sealed, 5000)!!.getString("t"))
        assertFalse(sealed.contains("get"))
    }

    @Test fun parsesLinksAndCodes() {
        val p = Pairing.parse("usage://pair?k=ZVQWYRKJHC65K6CJG524NTWH6U&r=wss%3A%2F%2Frelay.example&n=Meu%20PC")!!
        assertEquals("wss://relay.example", p.relayUrl)
        assertEquals("Meu PC", p.pcName)
        assertNotNull(Pairing.parse("zvqwy rkjhc-65k6c-jg524-ntwh6-u"))
        assertNull(Pairing.parse("abc"))
        assertEquals(Pairing.DEFAULT_RELAY, Pairing.parse(zero)!!.relayUrl)
    }

    @Test fun parsesSnapshotAndHidesMissingClis() {
        val s = SnapshotParser.parse(
            """{"schemaVersion":2,"generatedAt":"2026-10-31T01:00:00+00:00","providers":[
              {"id":"codex","name":"ChatGPT","status":"ok","fetchedAt":"2026-10-31T01:00:00+00:00","plan":"Plus","windows":[{"id":"five_hour","label":"5h","usedPercent":12,"resetsAt":"2026-10-31T06:00:00+00:00","durationMinutes":300}],"models":[],"details":[]},
              {"id":"opencode","name":"OpenCode","status":"error","errorCode":"CLI_NOT_FOUND","windows":[],"models":[],"details":[]}]}""")!!
        assertEquals(2, s.providers.size)
        assertEquals(12.0, s.providers[0].windows[0].percent!!, 0.0)
        assertTrue(s.providers[1].hidden)
        assertFalse(s.providers[0].hidden)
        assertNull(SnapshotParser.parse("""{"schemaVersion":1}"""))
    }

    @Test fun countdownFormats() {
        val now = 0L
        assertEquals("5h 18min", Fmt.countdown(now + (5 * 60 + 18) * 60_000L, now))
        assertEquals("3d 7h", Fmt.countdown(now + (3 * 24 * 60 + 7 * 60) * 60_000L, now))
        assertEquals("42min", Fmt.countdown(now + 42 * 60_000L, now))
        assertEquals("—", Fmt.countdown(null, now))
        assertEquals("68%", Fmt.percent(68.0))
        assertEquals("4.7%", Fmt.percent(4.66))
    }
}
