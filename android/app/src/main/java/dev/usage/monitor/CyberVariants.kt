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
    val soft: Boolean = false,
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
        CyberVariant("soft-slate", "Slate", true, 0xFF1D1F24.toInt(), 0xFF14161A.toInt(), 0xFFE6E7EC.toInt(), 0xFF9EA2AE.toInt(), 0xFF8FA6C9.toInt(), 0xFFA99FCB.toInt(), 0xFFD6B070.toInt(), 0xFFE0908C.toInt(), 0xFF1D1F24.toInt(), 0xFF20232A.toInt(), 0xFF1B1E23.toInt(), 0xFF8FA6C9.toInt(), 0xFFA99FCB.toInt(), 0xFF86BDA9.toInt(), 0xFFDBA283.toInt(), 0xFFB0A3D6.toInt(), true),
        CyberVariant("soft-dusk", "Dusk", true, 0xFF211F26.toInt(), 0xFF17151B.toInt(), 0xFFE8E6EC.toInt(), 0xFFA09DAC.toInt(), 0xFFA6A0D0.toInt(), 0xFF86AFC4.toInt(), 0xFFD6B070.toInt(), 0xFFE0908C.toInt(), 0xFF211F26.toInt(), 0xFF25222D.toInt(), 0xFF1E1D24.toInt(), 0xFFA6A0D0.toInt(), 0xFF86AFC4.toInt(), 0xFF86BDA9.toInt(), 0xFFDBA283.toInt(), 0xFFC0A8D6.toInt(), true),
        CyberVariant("soft-mist", "Mist", false, 0xFFF2F2F6.toInt(), 0xFFDADBE2.toInt(), 0xFF2A2C33.toInt(), 0xFF5F636E.toInt(), 0xFF4F6F9C.toInt(), 0xFF7B70A6.toInt(), 0xFF93660F.toInt(), 0xFFAB4440.toInt(), 0xFFF2F2F6.toInt(), 0xFFECEEF3.toInt(), 0xFFF5F3F0.toInt(), 0xFF4F6F9C.toInt(), 0xFF7B70A6.toInt(), 0xFF3F7F6D.toInt(), 0xFFA2653F.toInt(), 0xFF6F5FA3.toInt(), true),
        CyberVariant("soft-sand", "Sand", false, 0xFFF5F1EA.toInt(), 0xFFDFD8CC.toInt(), 0xFF2E2A25.toInt(), 0xFF665F55.toInt(), 0xFF5F7352.toInt(), 0xFF9A7A5A.toInt(), 0xFF8F6410.toInt(), 0xFFA8453C.toInt(), 0xFFF5F1EA.toInt(), 0xFFF0EBE2.toInt(), 0xFFF7F4EE.toInt(), 0xFF5F7352.toInt(), 0xFF9A7A5A.toInt(), 0xFF4A7F68.toInt(), 0xFFA5603A.toInt(), 0xFF71619B.toInt(), true),
        )
        fun get(id: String?) = all.firstOrNull { it.id == id && !it.soft } ?: all.first { !it.soft }
        fun getSoft(id: String?) = all.firstOrNull { it.id == id && it.soft } ?: all.first { it.soft }
    }
}
