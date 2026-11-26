package manifest

// IDScheme is the patch numbering that saved selections (Patchsu-CLI.json, patchsu-build.json) are written in.
// Scheme 1 is the first public release's numbering; scheme 2 (2026-11-26) moved the mod ports to 0500+, the custom
// server patches to 1000+ and closed the gaps. Files without a scheme are scheme 1.
const IDScheme = 2

// scheme1 maps the old patch ids to the current ones. Ids that kept their number (0001, 0002) aren't listed.
var scheme1 = map[string]string{
	"0003": "0500", "0004": "0501", "0005": "0003", "0005b": "0003b", "0006": "0004",
	"0007": "0005", "0007a": "0005", "0008": "0006", "0009": "0502", "0010": "0007",
	"0011": "0008", "0012": "0009", "0013": "0010", "0101": "1000",
	// 0007b / 0007c were merged into the Ritsu / Torii mod patches.
	"0007b": "", "0007c": "",
}

// MigrateIDs returns ids saved in the given scheme in the current numbering. Unknown ids are kept as they are.
func MigrateIDs(ids []string, scheme int) []string {
	if scheme >= IDScheme || len(ids) == 0 {
		return ids
	}
	out := make([]string, 0, len(ids))
	seen := map[string]bool{}
	for _, id := range ids {
		if n, ok := scheme1[id]; ok {
			id = n
		}
		if id != "" && !seen[id] {
			seen[id] = true
			out = append(out, id)
		}
	}
	return out
}
