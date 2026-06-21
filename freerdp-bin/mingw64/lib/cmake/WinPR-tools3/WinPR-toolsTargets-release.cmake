#----------------------------------------------------------------
# Generated CMake target import file for configuration "Release".
#----------------------------------------------------------------

# Commands may need to know the format version.
set(CMAKE_IMPORT_FILE_VERSION 1)

# Import target "winpr-tools" for configuration "Release"
set_property(TARGET winpr-tools APPEND PROPERTY IMPORTED_CONFIGURATIONS RELEASE)
set_target_properties(winpr-tools PROPERTIES
  IMPORTED_IMPLIB_RELEASE "${_IMPORT_PREFIX}/lib/libwinpr-tools3.dll.a"
  IMPORTED_LINK_DEPENDENT_LIBRARIES_RELEASE "winpr"
  IMPORTED_LOCATION_RELEASE "${_IMPORT_PREFIX}/bin/libwinpr-tools3.dll"
  )

list(APPEND _cmake_import_check_targets winpr-tools )
list(APPEND _cmake_import_check_files_for_winpr-tools "${_IMPORT_PREFIX}/lib/libwinpr-tools3.dll.a" "${_IMPORT_PREFIX}/bin/libwinpr-tools3.dll" )

# Commands beyond this point should not need to know the version.
set(CMAKE_IMPORT_FILE_VERSION)
