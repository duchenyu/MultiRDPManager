#----------------------------------------------------------------
# Generated CMake target import file for configuration "Release".
#----------------------------------------------------------------

# Commands may need to know the format version.
set(CMAKE_IMPORT_FILE_VERSION 1)

# Import target "freerdp-client" for configuration "Release"
set_property(TARGET freerdp-client APPEND PROPERTY IMPORTED_CONFIGURATIONS RELEASE)
set_target_properties(freerdp-client PROPERTIES
  IMPORTED_IMPLIB_RELEASE "${_IMPORT_PREFIX}/lib/libfreerdp-client3.dll.a"
  IMPORTED_LOCATION_RELEASE "${_IMPORT_PREFIX}/bin/libfreerdp-client3.dll"
  )

list(APPEND _cmake_import_check_targets freerdp-client )
list(APPEND _cmake_import_check_files_for_freerdp-client "${_IMPORT_PREFIX}/lib/libfreerdp-client3.dll.a" "${_IMPORT_PREFIX}/bin/libfreerdp-client3.dll" )

# Commands beyond this point should not need to know the version.
set(CMAKE_IMPORT_FILE_VERSION)
