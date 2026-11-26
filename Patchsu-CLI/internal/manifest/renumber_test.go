package manifest

import (
	"reflect"
	"testing"
)

func TestMigrateIDs(t *testing.T) {
	old := []string{"0001", "0003", "0005", "0005b", "0007a", "0007b", "0007", "0013", "0101", "x"}
	want := []string{"0001", "0500", "0003", "0003b", "0005", "0010", "1000", "x"}
	if got := MigrateIDs(old, 0); !reflect.DeepEqual(got, want) {
		t.Fatalf("MigrateIDs = %v, want %v", got, want)
	}
	if got := MigrateIDs([]string{"0005"}, IDScheme); !reflect.DeepEqual(got, []string{"0005"}) {
		t.Fatalf("current scheme must stay as it is, got %v", got)
	}
}
