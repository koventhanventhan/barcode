#!/usr/bin/env bash
set -euo pipefail

DOMAIN="${1:-${DOMAIN:-}}"
if [[ -z "${DOMAIN}" && -f ".env" ]]; then
  DOMAIN="$(grep -E '^DOMAIN=' .env | tail -n1 | cut -d= -f2- | tr -d '\r' | xargs || true)"
fi

if [[ -z "${DOMAIN}" ]]; then
  echo "Usage: DOMAIN=admino.lk $0  (or pass domain as first argument)" >&2
  exit 1
fi

BASE_URL="https://${DOMAIN}"

check_endpoint() {
  local name="$1"
  local url="$2"
  local expected="$3"
  local body

  echo "Checking ${name}: ${url}"
  body="$(curl -fsS --max-time 15 "${url}")"
  if [[ -n "${expected}" ]] && ! grep -q "${expected}" <<< "${body}"; then
    echo "FAIL ${name}: expected '${expected}' in response: ${body}" >&2
    exit 1
  fi
  echo "OK   ${name}"
}

check_endpoint "health" "${BASE_URL}/health" "Healthy"
check_endpoint "info" "${BASE_URL}/api/info" "product"
check_endpoint "public-branding" "${BASE_URL}/api/public/branding?code=" "displayName"

echo "All smoke checks passed for ${BASE_URL}"
