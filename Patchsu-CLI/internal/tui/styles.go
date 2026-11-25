package tui

import "github.com/charmbracelet/lipgloss"

var (
	accent  = lipgloss.AdaptiveColor{Light: "#C2185B", Dark: "#FF66AA"}
	dim     = lipgloss.AdaptiveColor{Light: "#8A8A8A", Dark: "#6C6C6C"}
	soft    = lipgloss.AdaptiveColor{Light: "#5A5A5A", Dark: "#A8A8A8"}
	okCol   = lipgloss.AdaptiveColor{Light: "#2E7D32", Dark: "#7BD88F"}
	badCol  = lipgloss.AdaptiveColor{Light: "#C62828", Dark: "#FF6B6B"}
	warnCol = lipgloss.AdaptiveColor{Light: "#B26A00", Dark: "#FFC66D"}

	sTitle   = lipgloss.NewStyle().Bold(true).Foreground(accent)
	sHeader  = lipgloss.NewStyle().Foreground(soft)
	sDim     = lipgloss.NewStyle().Foreground(dim)
	sSoft    = lipgloss.NewStyle().Foreground(soft)
	sBold    = lipgloss.NewStyle().Bold(true)
	sAccent  = lipgloss.NewStyle().Foreground(accent)
	sOK      = lipgloss.NewStyle().Foreground(okCol)
	sBad     = lipgloss.NewStyle().Foreground(badCol)
	sWarn    = lipgloss.NewStyle().Foreground(warnCol)
	sKeys    = lipgloss.NewStyle().Foreground(dim)
	sCursor  = lipgloss.NewStyle().Foreground(accent).Bold(true)
	sPreset  = lipgloss.NewStyle().Foreground(accent).Bold(true).Underline(true)
	sSection = lipgloss.NewStyle().Foreground(soft).Bold(true)
)

func rule(width int, title string) string {
	if width < 20 {
		width = 20
	}
	line := "─ " + title + " "
	n := width - lipgloss.Width(line)
	if n < 0 {
		n = 0
	}
	return sDim.Render(line + repeat("─", n))
}

func repeat(s string, n int) string {
	out := make([]byte, 0, len(s)*n)
	for i := 0; i < n; i++ {
		out = append(out, s...)
	}
	return string(out)
}
