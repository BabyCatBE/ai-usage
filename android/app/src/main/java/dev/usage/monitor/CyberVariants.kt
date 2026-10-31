package dev.usage.monitor

/**
 * One sub-theme of the cyberpunk look (several dark, several light). Primary and secondary draw the neon rim, ticks and glow;
 * the three bg stops paint the backdrop; codex/claude/open are the provider accents.
 */
class CyberVariant(
    val id: String, val name: String, val dark: Boolean,
    val base: Int, val ink: Int, val text: Int, val dim: Int,
    val primary: Int, val secondary: Int, val warn: Int, val crit: Int,
    val bgA: Int, val bgB: Int, val bgC: Int, val glow1: Int, val glow2: Int,
    val codex: Int, val claude: Int, val open: Int,
) {
    companion object {
        val all = listOf(
        CyberVariant("neon-night", "Neon Night", true, 0xFF0B0714.toInt(), 0xFF05030B.toInt(), 0xFFEAF6FF.toInt(), 0xFF9FB0E8.toInt(), 0xFF00E5FF.toInt(), 0xFFFF2BD6.toInt(), 0xFFFCEE0A.toInt(), 0xFFFF2E6E.toInt(), 0xFF0B0714.toInt(), 0xFF14072A.toInt(), 0xFF05101E.toInt(), 0xFFFF2BD6.toInt(), 0xFF00E5FF.toInt(), 0xFF00FFB2.toInt(), 0xFFFF7A1A.toInt(), 0xFFC76BFF.toInt()),
        CyberVariant("black-yellow", "Black & Yellow", true, 0xFF0A0A08.toInt(), 0xFF000000.toInt(), 0xFFFFF8D6.toInt(), 0xFFC9BE8A.toInt(), 0xFFFCEE0A.toInt(), 0xFFF5A300.toInt(), 0xFFFF9F1C.toInt(), 0xFFFF3B30.toInt(), 0xFF0A0A08.toInt(), 0xFF15140A.toInt(), 0xFF0A0A08.toInt(), 0xFFFCEE0A.toInt(), 0xFFF5A300.toInt(), 0xFF00F0FF.toInt(), 0xFFFF6A00.toInt(), 0xFFFF3EA5.toInt()),
        CyberVariant("purple-blue", "Purple Haze", true, 0xFF0C0824.toInt(), 0xFF05031A.toInt(), 0xFFEEEBFF.toInt(), 0xFFA9A6E8.toInt(), 0xFF8B5CF6.toInt(), 0xFF38BDF8.toInt(), 0xFFFBBF24.toInt(), 0xFFFB4B7B.toInt(), 0xFF0C0824.toInt(), 0xFF1A0B4A.toInt(), 0xFF07183A.toInt(), 0xFF8B5CF6.toInt(), 0xFF38BDF8.toInt(), 0xFF34F5C5.toInt(), 0xFFFF8A5B.toInt(), 0xFFE879F9.toInt()),
        CyberVariant("toxic-green", "Toxic Matrix", true, 0xFF04100A.toInt(), 0xFF010805.toInt(), 0xFFE6FFEE.toInt(), 0xFF8FD1A6.toInt(), 0xFF39FF14.toInt(), 0xFF00E5A8.toInt(), 0xFFFFD60A.toInt(), 0xFFFF3B5C.toInt(), 0xFF03100A.toInt(), 0xFF05200F.toInt(), 0xFF021A18.toInt(), 0xFF39FF14.toInt(), 0xFF00E5A8.toInt(), 0xFF7CFF6B.toInt(), 0xFFFF9A3D.toInt(), 0xFF5CE1FF.toInt()),
        CyberVariant("crimson", "Crimson Blade", true, 0xFF0F0508.toInt(), 0xFF070204.toInt(), 0xFFFFEDEF.toInt(), 0xFFD69AA4.toInt(), 0xFFFF1F5A.toInt(), 0xFFB02BFF.toInt(), 0xFFFFC107.toInt(), 0xFFFF6B6B.toInt(), 0xFF0F0508.toInt(), 0xFF2A0714.toInt(), 0xFF140A24.toInt(), 0xFFFF1F5A.toInt(), 0xFF7A2BFF.toInt(), 0xFF00F0FF.toInt(), 0xFFFFA94D.toInt(), 0xFFD16BFF.toInt()),
        CyberVariant("pop-light", "Pop Pink & Cyan", false, 0xFFF4F0FF.toInt(), 0xFFD8D2F0.toInt(), 0xFF2A1B4D.toInt(), 0xFF6B5B95.toInt(), 0xFFE01EA8.toInt(), 0xFF00A8C6.toInt(), 0xFFD98300.toInt(), 0xFFD6204A.toInt(), 0xFFEDE7FF.toInt(), 0xFFFBE7F5.toInt(), 0xFFDDF6FB.toInt(), 0xFFFF7AD9.toInt(), 0xFF7AE7FF.toInt(), 0xFF00A37A.toInt(), 0xFFE8590C.toInt(), 0xFF7C3AED.toInt()),
        CyberVariant("sun-light", "Sunny Gold & Black", false, 0xFFFFFCE8.toInt(), 0xFFEBE2A8.toInt(), 0xFF15130A.toInt(), 0xFF6B6330.toInt(), 0xFFD9A400.toInt(), 0xFF111111.toInt(), 0xFFB26A00.toInt(), 0xFFC62828.toInt(), 0xFFFFF9CC.toInt(), 0xFFFFFDF0.toInt(), 0xFFFFF1A8.toInt(), 0xFFFFE600.toInt(), 0xFFFFB300.toInt(), 0xFF00796B.toInt(), 0xFFD84300.toInt(), 0xFF6A1B9A.toInt()),
        CyberVariant("lavender-light", "Lavender Circuit", false, 0xFFEEF2FF.toInt(), 0xFFD6DBF5.toInt(), 0xFF1B1B3A.toInt(), 0xFF5B5F97.toInt(), 0xFF6D28D9.toInt(), 0xFF2563EB.toInt(), 0xFFC77700.toInt(), 0xFFD91E4A.toInt(), 0xFFE8ECFF.toInt(), 0xFFF0E8FF.toInt(), 0xFFE0F0FF.toInt(), 0xFFA78BFA.toInt(), 0xFF60A5FA.toInt(), 0xFF0D9488.toInt(), 0xFFEA580C.toInt(), 0xFF9333EA.toInt()),
        CyberVariant("mint-light", "Mint Neon", false, 0xFFECFDF5.toInt(), 0xFFC7EEDC.toInt(), 0xFF06281C.toInt(), 0xFF3F7A62.toInt(), 0xFF059669.toInt(), 0xFF0891B2.toInt(), 0xFFB45309.toInt(), 0xFFDC2626.toInt(), 0xFFE4FBF0.toInt(), 0xFFEFFEF8.toInt(), 0xFFDAF5F7.toInt(), 0xFF34D399.toInt(), 0xFF22D3EE.toInt(), 0xFF047857.toInt(), 0xFFEA580C.toInt(), 0xFF7C3AED.toInt()),
        )
        fun get(id: String?) = all.firstOrNull { it.id == id } ?: all[0]
    }
}
