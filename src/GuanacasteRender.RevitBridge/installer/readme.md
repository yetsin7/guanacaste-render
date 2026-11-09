# Guanacaste 3D exporter for Revit 1.0.1

Extract the entire ZIP and run `install.cmd`. The package includes the versions
listed in its RevitYYYY folders; this release ships Revit 2027 only.
Restart Revit after installation. The add-in is installed for the current user;
The bundled .NET 10 Desktop runtime is needed only if optional thumbnails are enabled;
Windows may request administrator credentials for that prerequisite.

The Guanacaste Render ribbon exports the whole model or the active 3D view to `.g3d` (Guanacaste 3D) or `.gr` (Guanacaste Render).
Both project extensions contain the Revit exchange data and are converted to an editable project when opened. Existing `.g3dx`
exchange files remain importable for compatibility.
The included optional Windows thumbnail component can preview `.g3d`, `.gr`, `.g3dt`, and `.g3df` files, but is **disabled by default** because it currently runs
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
