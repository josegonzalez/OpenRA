#!/bin/sh
# Fetches a pinned prebuilt ANGLE (Metal backend) as libEGL/libGLESv2 xcframeworks.
# Building ANGLE from source takes ~30 minutes and ~10 GB of tooling, so a
# checksummed prebuilt unblocks development; build-angle.sh is the reproducible path.
#
# Set ANGLE_URL to an archive containing libEGL.xcframework and libGLESv2.xcframework
# at its top level, and ANGLE_SHA256 to its checksum.
set -e

ANGLE_URL="${ANGLE_URL:-}"
ANGLE_SHA256="${ANGLE_SHA256:-}"

DIR="$(cd "$(dirname "$0")" && pwd)"
NATIVE="${DIR}/native"
WORK="${DIR}/build"
mkdir -p "${NATIVE}" "${WORK}"

if [ -z "${ANGLE_URL}" ] || [ -z "${ANGLE_SHA256}" ]; then
	echo "ANGLE_URL and ANGLE_SHA256 must be set to a prebuilt ANGLE iOS archive."
	echo "Alternatively run build-angle.sh to build from source."
	exit 1
fi

archive="${WORK}/angle-prebuilt.tar.gz"
if [ ! -e "${archive}" ]; then
	echo "Fetching ANGLE..."
	curl -sSfL "${ANGLE_URL}" -o "${archive}"
fi

echo "${ANGLE_SHA256}  ${archive}" | shasum -a 256 -c - || { echo "ANGLE archive checksum mismatch"; exit 1; }

rm -rf "${WORK}/angle" "${NATIVE}/libEGL.xcframework" "${NATIVE}/libGLESv2.xcframework"
mkdir -p "${WORK}/angle"
tar -xzf "${archive}" -C "${WORK}/angle"

for name in libEGL libGLESv2; do
	found="$(find "${WORK}/angle" -type d -name "${name}.xcframework" | head -n1)"
	if [ -z "${found}" ]; then
		echo "${name}.xcframework not found in the archive"
		exit 1
	fi

	cp -R "${found}" "${NATIVE}/"
	echo "Installed ${NATIVE}/${name}.xcframework"
done
