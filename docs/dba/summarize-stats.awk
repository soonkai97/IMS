# Summarise sqlcmd output of the experiment scripts (SET STATISTICS IO, TIME ON): one line per statement = LAST run of each label (CPU and elapsed include parse and compile time).
/^### / { if (label != "") emit(); label = substr($0, 5); reads = 0; cpu = 0; el = 0; next }
/logical reads/ { if (match($0, /logical reads [0-9]+/)) { s = substr($0, RSTART + 14, RLENGTH - 14); reads += s } }
/SQL Server (Execution|parse and compile) [Tt]ime/ { getline nxt; if (match(nxt, /CPU time = [0-9]+/)) cpu += substr(nxt, RSTART + 11, RLENGTH - 11); if (match(nxt, /elapsed time = [0-9]+/)) el += substr(nxt, RSTART + 15, RLENGTH - 15) }
END { emit() }
function emit() { sub(/ #[0-9]$/, "", label); last[label] = sprintf("%-42s reads %9d  cpu %6d ms  elapsed %6d ms", label, reads, cpu, el); if (!(label in seen)) { seen[label] = 1; order[++n] = label } }
END { for (i = 1; i <= n; i++) print last[order[i]] }
