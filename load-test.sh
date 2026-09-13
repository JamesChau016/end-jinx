#!/usr/bin/env bash

set -u

HOST="127.0.0.1"
PORT=8000
TIMEOUT=10
REQUESTS_PER_LEVEL=100
LEVELS=(1 10 25 50 100 250)
KEEP_ALIVE=0

usage() {
    cat <<'EOF'
Usage: ./load-test.sh [options]

Options:
  -h, --host HOST       Server host, default: 127.0.0.1
  -p, --port PORT       Server port, default: 8000
  -t, --timeout SEC     Per-request timeout, default: 10
  -n, --requests COUNT  Requests per concurrency level, default: 100
  -l, --levels LIST     Comma-separated levels, default: 1,10,25,50,100,250
  -k, --keep-alive      Send Connection: keep-alive
      --help            Show this help

Examples:
  ./load-test.sh
  ./load-test.sh -l 10,50,100,250 -n 500
  ./load-test.sh --keep-alive -l 25,100,250
EOF
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        -h|--host) HOST="$2"; shift 2 ;;
        -p|--port) PORT="$2"; shift 2 ;;
        -t|--timeout) TIMEOUT="$2"; shift 2 ;;
        -n|--requests) REQUESTS_PER_LEVEL="$2"; shift 2 ;;
        -l|--levels) IFS=',' read -r -a LEVELS <<< "$2"; shift 2 ;;
        -k|--keep-alive) KEEP_ALIVE=1; shift ;;
        --help) usage; exit 0 ;;
        *) echo "Unknown option: $1" >&2; usage >&2; exit 2 ;;
    esac
done

if ! command -v curl >/dev/null 2>&1; then
    echo "curl is required but was not found." >&2
    exit 1
fi

URL="http://${HOST}:${PORT}/"
WORK_DIR="$(mktemp -d)"
trap 'rm -rf "$WORK_DIR"' EXIT

if (( KEEP_ALIVE == 1 )); then
    CONNECTION_HEADER="keep-alive"
else
    CONNECTION_HEADER="close"
fi

run_request() {
    local index="$1"
    local output_file="$2"
    local start end elapsed http_code curl_status

    start=$(date +%s%N 2>/dev/null || date +%s)
    curl --silent --show-error --http1.1 \
        --connect-timeout "$TIMEOUT" \
        --max-time "$TIMEOUT" \
        -H "Connection: ${CONNECTION_HEADER}" \
        -D "${output_file}.headers" \
        -o /dev/null \
        "$URL" 2>"${output_file}.error"
    curl_status=$?
    http_code=$(awk 'NR == 1 { print $2; exit }' "${output_file}.headers" 2>/dev/null || true)
    http_code="${http_code:-000}"
    end=$(date +%s%N 2>/dev/null || date +%s)

    if [[ "$start" =~ ^[0-9]+$ && "$end" =~ ^[0-9]+$ ]]; then
        elapsed=$((end - start))
        if (( elapsed > 1000000 )); then
            elapsed="$(awk "BEGIN { printf \"%.3f\", ${elapsed}/1000000000 }")"
        fi
    else
        elapsed="?"
    fi

    if (( curl_status == 0 && http_code == 200 )); then
        printf 'success %s %s\n' "$elapsed" "$http_code" > "$output_file"
    else
        local error
        error=$(tr '\n' ' ' < "${output_file}.error")
        printf 'failure %s %s %s\n' "$elapsed" "$http_code" "${error:-curl_exit_${curl_status}}" > "$output_file"
    fi
}

export HOST PORT TIMEOUT URL CONNECTION_HEADER
export -f run_request

echo "Testing ${URL}"
echo "Requests per level: ${REQUESTS_PER_LEVEL}"
echo "Connection header: ${CONNECTION_HEADER}"
echo

for level in "${LEVELS[@]}"; do
    level_dir="${WORK_DIR}/${level}"
    mkdir -p "$level_dir"
    start_seconds=$(date +%s)

    seq "$REQUESTS_PER_LEVEL" | xargs -I {} -P "$level" bash -c \
        'run_request "$1" "${2}/${1}.result"' _ {} "$level_dir"

    end_seconds=$(date +%s)
    success_count=$(cat "$level_dir"/*.result 2>/dev/null | grep -c '^success ' || true)
    failure_count=$(cat "$level_dir"/*.result 2>/dev/null | grep -c '^failure ' || true)
    total_seconds=$((end_seconds - start_seconds))
    total_count=$((success_count + failure_count))

    if (( total_seconds > 0 )); then
        rate=$((total_count / total_seconds))
    else
        rate="<1s"
    fi

    average=$(awk '/^success / { total += $2; count++ } END { if (count > 0) printf "%.3f", total / count; else print "-" }' "$level_dir"/*.result 2>/dev/null)

    printf 'Concurrency %4s: success=%4s failure=%4s avg=%7s s rate=%s/s\n' \
        "$level" "$success_count" "$failure_count" "$average" "$rate"

    if (( failure_count > 0 )); then
        echo "  Example failure:"
        grep '^failure ' "$level_dir"/*.result | head -n 1 | cut -d' ' -f4-
    fi
done
