# Guanacaste 3D exporter for Revit 1.0.1

Extract the entire ZIP and run `install.cmd`. The package includes the versions
listed in its RevitYYYY folders; this release ships Revit 2027 only.
Restart Revit after installation. The add-in is installed for the current user;
The bundled .NET 10 Desktop runtime is needed only if optional thumbnails are enabled;
Windows may request administrator credentials for that prerequisite.

The Guanacaste 3D ribbon exports the whole model or the active 3D view to `.g3dx`.
New files contain a 512-pixel preview. The included optional Windows thumbnail component can preview older `.g3dx`, `.g3d`,
`.g3dt`, and `.g3df` files, but is **disabled by default** because it currently runs
inside Explorer and can continue work after a timeout. To accept that risk, run
`./install.ps1 -EnableThumbnails` in PowerShell from the extracted directory.
Refresh Explorer with F5 afterward. The desktop application is needed to open/edit
models; it is not needed to display optional thumbnails.

Close Revit and run `uninstall.cmd` to remove the exporter. If you opted in to
Shell thumbnails, remove them separately with
`powershell -File register-thumbnails.ps1 -Unregister`.

The exporter preserves editable walls, hosted openings and supported slabs;
other geometry becomes mesh families when imported. This is not full Revit parity.
The optional preview uses exported geometry, not a screenshot of the Revit window.
