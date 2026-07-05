/*
 * SDL2-CS binds the full SDL API surface, including entry points for other
 * platforms that the iOS build of SDL2 does not provide. Direct P/Invoke
 * resolution requires every bound symbol to exist at link time; these are
 * never called on iOS, so they only need to be present.
 */

void *SDL_AndroidGetActivity(void) { return 0; }
void *SDL_AndroidGetJNIEnv(void) { return 0; }
const char *SDL_AndroidGetExternalStoragePath(void) { return 0; }
int SDL_AndroidGetExternalStorageState(void) { return 0; }
const char *SDL_AndroidGetInternalStoragePath(void) { return 0; }
int SDL_AndroidRequestPermission(const char *permission) { return 0; }
int SDL_AndroidShowToast(const char *message, int duration, int gravity, int xoffset, int yoffset) { return -1; }
void SDL_AndroidBackButton(void) { }
int SDL_GetAndroidSDKVersion(void) { return 0; }
int SDL_IsAndroidTV(void) { return 0; }
int SDL_IsChromebook(void) { return 0; }
int SDL_GDKRunApp(void *mainFunction, void *reserved) { return -1; }
int SDL_IsDeXMode(void) { return 0; }
void *SDL_RenderGetD3D11Device(void *renderer) { return 0; }
void *SDL_RenderGetD3D9Device(void *renderer) { return 0; }
void SDL_SetWindowsMessageHook(void *callback, void *userdata) { }
int SDL_WinRTGetDeviceFamily(void) { return 0; }
int SDL_WinRTRunApp(void *mainFunction, void *reserved) { return -1; }
