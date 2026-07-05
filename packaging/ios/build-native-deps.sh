#!/bin/sh
# Builds the static native dependencies (SDL2, OpenAL Soft, FreeType, Lua 5.1)
# as xcframeworks with device and simulator slices for the iOS app.
# Requires: Xcode command line tools, cmake, git, curl.
set -e

SDL2_VERSION="2.30.11"
OPENAL_VERSION="1.24.2"
FREETYPE_VERSION="2.13.3"
LUA_VERSION="5.1.5"

IOS_DEPLOYMENT_TARGET="17.0"

DIR="$(cd "$(dirname "$0")" && pwd)"
NATIVE="${DIR}/native"
WORK="${DIR}/build"
mkdir -p "${NATIVE}" "${WORK}"

command -v cmake >/dev/null || { echo "cmake is required (brew install cmake)"; exit 1; }
command -v xcodebuild >/dev/null || { echo "Xcode is required"; exit 1; }

fetch() {
	name="$1"
	url="$2"
	archive="${WORK}/${name}.tar.gz"
	if [ ! -e "${archive}" ]; then
		echo "Fetching ${name}..."
		curl -sSfL --retry 3 --retry-delay 2 "${url}" -o "${archive}"
	fi

	rm -rf "${WORK}/${name}"
	mkdir -p "${WORK}/${name}"
	tar -xzf "${archive}" -C "${WORK}/${name}" --strip-components=1
}

# Builds one cmake project for one SDK, leaving the static library at the echoed path
cmake_build() {
	src="$1"
	sdk="$2"
	shift 2

	build="${src}/build-${sdk}"
	cmake -S "${src}" -B "${build}" \
		-DCMAKE_SYSTEM_NAME=iOS \
		-DCMAKE_OSX_SYSROOT="${sdk}" \
		-DCMAKE_OSX_ARCHITECTURES=arm64 \
		-DCMAKE_OSX_DEPLOYMENT_TARGET="${IOS_DEPLOYMENT_TARGET}" \
		-DCMAKE_BUILD_TYPE=Release \
		-DBUILD_SHARED_LIBS=OFF \
		"$@" > "${build}.log" 2> "${build}.err.log"
	cmake --build "${build}" --config Release -j8 >> "${build}.log" 2>> "${build}.err.log"
}

already_built() {
	if [ -d "${NATIVE}/$1.xcframework" ]; then
		echo "Skipping $1: ${NATIVE}/$1.xcframework already exists"
		return 0
	fi

	return 1
}

make_xcframework() {
	name="$1"
	device_lib="$2"
	simulator_lib="$3"
	headers="$4"

	rm -rf "${NATIVE}/${name}.xcframework"
	if [ -n "${headers}" ]; then
		xcodebuild -create-xcframework \
			-library "${device_lib}" -headers "${headers}" \
			-library "${simulator_lib}" -headers "${headers}" \
			-output "${NATIVE}/${name}.xcframework"
	else
		xcodebuild -create-xcframework \
			-library "${device_lib}" \
			-library "${simulator_lib}" \
			-output "${NATIVE}/${name}.xcframework"
	fi

	echo "Created ${NATIVE}/${name}.xcframework"
}

echo "=== SDL2 ${SDL2_VERSION} ==="
already_built SDL2 || {
fetch sdl2 "https://github.com/libsdl-org/SDL/releases/download/release-${SDL2_VERSION}/SDL2-${SDL2_VERSION}.tar.gz"
for sdk in iphoneos iphonesimulator; do
	cmake_build "${WORK}/sdl2" "${sdk}" -DSDL_STATIC=ON -DSDL_SHARED=OFF -DSDL_TEST=OFF

	# Append stubs for the non-iOS entry points that the SDL2-CS binding references
	sysroot="$(xcrun --sdk "${sdk}" --show-sdk-path)"
	xcrun --sdk "${sdk}" clang -arch arm64 -isysroot "${sysroot}" -O2 \
		-c "${DIR}/sdl2-ios-stubs.c" -o "${WORK}/sdl2/build-${sdk}/sdl2-ios-stubs.o"
	xcrun --sdk "${sdk}" libtool -static -o "${WORK}/sdl2/build-${sdk}/libSDL2-full.a" \
		"${WORK}/sdl2/build-${sdk}/libSDL2.a" "${WORK}/sdl2/build-${sdk}/sdl2-ios-stubs.o"
done
make_xcframework SDL2 \
	"${WORK}/sdl2/build-iphoneos/libSDL2-full.a" \
	"${WORK}/sdl2/build-iphonesimulator/libSDL2-full.a" \
	"${WORK}/sdl2/include"
}

echo "=== OpenAL Soft ${OPENAL_VERSION} ==="
already_built openal-soft || {
fetch openal-soft "https://github.com/kcat/openal-soft/archive/refs/tags/${OPENAL_VERSION}.tar.gz"
for sdk in iphoneos iphonesimulator; do
	cmake_build "${WORK}/openal-soft" "${sdk}" \
		-DLIBTYPE=STATIC -DALSOFT_REQUIRE_COREAUDIO=ON -DALSOFT_UTILS=OFF -DALSOFT_EXAMPLES=OFF \
		-DALSOFT_BACKEND_WAVE=OFF
done
make_xcframework openal-soft \
	"${WORK}/openal-soft/build-iphoneos/libopenal.a" \
	"${WORK}/openal-soft/build-iphonesimulator/libopenal.a" \
	"${WORK}/openal-soft/include"
}

echo "=== FreeType ${FREETYPE_VERSION} ==="
already_built freetype || {
fetch freetype "https://download.savannah.gnu.org/releases/freetype/freetype-${FREETYPE_VERSION}.tar.gz"
for sdk in iphoneos iphonesimulator; do
	cmake_build "${WORK}/freetype" "${sdk}" \
		-DFT_DISABLE_HARFBUZZ=ON -DFT_DISABLE_BROTLI=ON -DFT_DISABLE_PNG=ON \
		-DFT_DISABLE_BZIP2=ON -DFT_DISABLE_ZLIB=ON
done
make_xcframework freetype \
	"${WORK}/freetype/build-iphoneos/libfreetype.a" \
	"${WORK}/freetype/build-iphonesimulator/libfreetype.a" \
	"${WORK}/freetype/include"
}

echo "=== Lua ${LUA_VERSION} ==="
already_built lua51 || {
fetch lua "https://www.lua.org/ftp/lua-${LUA_VERSION}.tar.gz"

# iOS forbids system(): os.execute always reports that no shell is available
sed -i '' \
	's|lua_pushinteger(L, system(luaL_optstring(L, 1, NULL)));|lua_pushinteger(L, (luaL_optstring(L, 1, NULL) != NULL ? -1 : 0));|' \
	"${WORK}/lua/src/loslib.c"
grep -q "system(" "${WORK}/lua/src/loslib.c" && { echo "loslib.c patch failed"; exit 1; }

for sdk in iphoneos iphonesimulator; do
	sysroot="$(xcrun --sdk "${sdk}" --show-sdk-path)"
	if [ "${sdk}" = "iphoneos" ]; then
		version_flag="-miphoneos-version-min=${IOS_DEPLOYMENT_TARGET}"
	else
		version_flag="-mios-simulator-version-min=${IOS_DEPLOYMENT_TARGET}"
	fi

	build="${WORK}/lua/build-${sdk}"
	mkdir -p "${build}"
	for src in "${WORK}"/lua/src/*.c; do
		base="$(basename "${src}" .c)"
		# lua.c and luac.c are the standalone interpreters, not the library
		if [ "${base}" = "lua" ] || [ "${base}" = "luac" ]; then
			continue
		fi

		xcrun --sdk "${sdk}" clang -arch arm64 -isysroot "${sysroot}" ${version_flag} \
			-O2 -DLUA_USE_POSIX -c "${src}" -o "${build}/${base}.o"
	done
	xcrun --sdk "${sdk}" libtool -static -o "${build}/liblua51.a" "${build}"/*.o
done
make_xcframework lua51 \
	"${WORK}/lua/build-iphoneos/liblua51.a" \
	"${WORK}/lua/build-iphonesimulator/liblua51.a" \
	""
}

echo "All native dependencies are in ${NATIVE}"
echo "ANGLE (libEGL/libGLESv2) is fetched separately: see fetch-angle.sh"
