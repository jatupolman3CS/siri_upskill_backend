#!/usr/bin/env bash
set -euo pipefail

SCENARIO="${1:-search}"
BASE_URL="${2:-http://localhost:5190}"

echo "=========================================================="
echo "       SiriUpSkill Performance & Load Testing Runner      "
echo "=========================================================="
echo "Target Base URL: $BASE_URL"
echo "Scenario:        $SCENARIO"
echo ""

if ! command -v k6 &> /dev/null; then
    echo "Error: k6 is not installed or not in PATH."
    echo "Install k6 via: sudo gpg -k && sudo apt-key ... or https://k6.io/docs/get-started/installation/"
    exit 1
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

run_test() {
    local script="$1"
    local name="$2"
    echo ">>> Running scenario: $name ($script)"
    BASE_URL="$BASE_URL" k6 run "$SCRIPT_DIR/$script"
}

case "$SCENARIO" in
    search)
        run_test "search-burst.js" "Search Burst (200 req/sec)"
        ;;
    checkout)
        run_test "checkout-burst.js" "Checkout Flash Burst (500 checkouts/min)"
        ;;
    viewer)
        run_test "viewer-stream.js" "Viewer Stream (5,000 concurrent viewers)"
        ;;
    all)
        run_test "search-burst.js" "Search Burst"
        run_test "checkout-burst.js" "Checkout Flash Burst"
        run_test "viewer-stream.js" "Viewer Stream"
        ;;
    *)
        echo "Unknown scenario: $SCENARIO. Choose search, checkout, viewer, or all."
        exit 1
        ;;
esac

echo ""
echo "Load test run completed successfully."
