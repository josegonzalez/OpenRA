#!/bin/sh
# Builds ANGLE's Metal backend from source into libEGL/libGLESv2 xcframeworks.
# This is the reproducible alternative to fetch-angle.sh. The first run downloads
# depot_tools and the ANGLE source tree (several GB) and takes ~30 minutes.
set -e

# Update to a newer tested commit when refreshing the dependency
ANGLE_COMMIT="${ANGLE_COMMIT:-main}"
IOS_DEPLOYMENT_TARGET="17.0"

DIR="$(cd "$(dirname "$0")" && pwd)"
NATIVE="${DIR}/native"
WORK="${DIR}/build"
mkdir -p "${NATIVE}" "${WORK}"

if [ ! -d "${WORK}/depot_tools" ]; then
	git clone --depth 1 https://chromium.googlesource.com/chromium/tools/depot_tools.git "${WORK}/depot_tools"
fi
PATH="${WORK}/depot_tools:${PATH}"
export PATH

if [ ! -d "${WORK}/angle-src" ]; then
	git clone https://chromium.googlesource.com/angle/angle "${WORK}/angle-src"
fi

cd "${WORK}/angle-src"
git fetch origin "${ANGLE_COMMIT}" || true
git checkout "${ANGLE_COMMIT}"
python3 scripts/bootstrap.py
gclient sync

build_slice() {
	target_env="$1" # device | simulator
	out="out/ios-${target_env}"

	# gn rejects tab characters in the args string
	gn gen "${out}" --args="target_os = \"ios\" target_cpu = \"arm64\" target_environment = \"${target_env}\" ios_deployment_target = \"${IOS_DEPLOYMENT_TARGET}\" is_debug = false angle_enable_metal = true angle_enable_vulkan = false angle_enable_gl = false angle_enable_d3d9 = false angle_enable_d3d11 = false angle_build_tests = false build_with_chromium = false ios_enable_code_signing = false"

	autoninja -C "${out}" libEGL libGLESv2
}

build_slice device
build_slice simulator

for name in libEGL libGLESv2; do
	rm -rf "${NATIVE}/${name}.xcframework"
	xcodebuild -create-xcframework \
		-framework "out/ios-device/${name}.framework" \
		-framework "out/ios-simulator/${name}.framework" \
		-output "${NATIVE}/${name}.xcframework"
	echo "Created ${NATIVE}/${name}.xcframework"
done
