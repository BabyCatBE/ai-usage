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

    // ---- beta pace projection ------------------------------------------------------------------------------

    private val h = 3_600_000L
    private fun sample(minAgo: Long, pct: Double, reset: Long = 10 * h) = Projection.Sample(10 * h - minAgo * 60_000L, pct, reset)

    @Test fun projectsRunOutFromTheLastHour() {
        // 5-hour window: 20 % an hour ago, 50 % now, reset in 3 h -> 30 %/h, the remaining 50 % lasts 1 h 40 min
        val now = 10 * h
        val r = Projection.project(listOf(sample(70, 15.0), sample(60, 20.0), sample(30, 35.0), sample(0, 50.0)), 50.0, now + 3 * h, now)!!
        assertEquals(30.0, r.perHour, 0.01)
        assertEquals(now + 100 * 60_000L, r.runsOutAtMs)
        assertNull(r.atResetPercent)
    }

    @Test fun projectsPercentAtResetWhenItLasts() {
        val now = 10 * h
        val r = Projection.project(listOf(sample(60, 40.0), sample(0, 45.0)), 45.0, now + 2 * h, now)!!
        assertNull(r.runsOutAtMs)
        assertEquals(55.0, r.atResetPercent!!, 0.01)
    }

    @Test fun ignoresSamplesBeforeAResetAndNeedsHistory() {
        val now = 10 * h
        // the window reset 20 minutes ago (percentage dropped): only the run since then counts
        val r = Projection.project(listOf(sample(60, 90.0), sample(20, 2.0), sample(0, 6.0)), 6.0, now + 5 * h, now)!!
        assertEquals(20, r.basisMinutes.toInt())
        assertEquals(12.0, r.perHour, 0.01)
        assertNull(Projection.project(listOf(sample(5, 3.0), sample(0, 4.0)), 4.0, now + h, now))
        assertEquals(0.0, Projection.project(listOf(sample(60, 4.0), sample(0, 4.0)), 4.0, now + h, now)!!.perHour, 0.0)
    }

    @Test fun historyRoundTripsAndDropsOldSamples() {
        val snap = SnapshotParser.parse("""{"schemaVersion":2,"generatedAt":"2026-10-31T01:00:00+00:00","providers":[
              {"id":"claude","name":"Claude","status":"ok","fetchedAt":"2026-10-31T01:00:00+00:00","windows":[{"id":"five_hour","label":"5h","usedPercent":12,"resetsAt":"2026-10-31T06:00:00+00:00"}],"models":[],"details":[]}]}""")!!
        val at = snap.generatedAtMs
        val once = Projection.append(null, snap, at)
        val twice = Projection.append(once, snap, at)   // same reading again: not duplicated
        assertEquals(1, Projection.read(twice, "claude/five_hour").size)
        assertEquals(12.0, Projection.read(twice, "claude/five_hour")[0].percent, 0.0)
        assertTrue(Projection.read(Projection.append(twice, snap.copy(providers = emptyList()), at + 9 * h), "claude/five_hour").isEmpty())
    }
}
