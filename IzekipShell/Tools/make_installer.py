r"""Tek dosyalik kurulum exe'si: release zip'ini ve kur.cmd'yi IExpress (Windows'un
kendi SFX araci) ile sarar. Cift tiklaninca zip'i %LocalAppData%\Programs\IzekipShell'e
acar, Baslat menusune kisayol birakir ve uygulamayi baslatir.

Kullanim: python Tools\make_installer.py <release_zip_yolu> [cikti_exe_yolu]
"""
import os
import shutil
import subprocess
import sys
import tempfile

HERE = os.path.dirname(__file__)


def main():
    if len(sys.argv) < 2:
        print("kullanim: make_installer.py <release.zip> [cikti.exe]")
        sys.exit(1)

    zip_path = os.path.abspath(sys.argv[1])
    out_path = os.path.abspath(sys.argv[2]) if len(sys.argv) > 2 else os.path.join(HERE, "..", "..", "IzekipShell-Setup.exe")
    out_path = os.path.abspath(out_path)

    with tempfile.TemporaryDirectory(prefix="izekip-sfx-") as stage:
        shutil.copy(zip_path, os.path.join(stage, "izekip.zip"))
        shutil.copy(os.path.join(HERE, "kur.cmd"), os.path.join(stage, "kur.cmd"))

        sed_path = os.path.join(stage, "paket.sed")
        sed = f"""[Version]
Class=IEXPRESS
SEDVersion=3
[Options]
PackagePurpose=InstallApp
ShowInstallProgramWindow=0
HideExtractAnimation=1
UseLongFileName=1
InsideCompressed=0
CAB_FixedSize=0
CAB_ResvCodeSigning=0
RebootMode=N
InstallPrompt=%InstallPrompt%
DisplayLicense=%DisplayLicense%
FinishMessage=%FinishMessage%
TargetName=%TargetName%
FriendlyName=%FriendlyName%
AppLaunched=%AppLaunched%
PostInstallCmd=%PostInstallCmd%
AdminQuietInstCmd=%AdminQuietInstCmd%
UserQuietInstCmd=%UserQuietInstCmd%
SourceFiles=SourceFiles
[Strings]
InstallPrompt=
DisplayLicense=
FinishMessage=
TargetName={out_path}
FriendlyName=Izekip Shell Kurulumu
AppLaunched=kur.cmd
PostInstallCmd=<None>
AdminQuietInstCmd=
UserQuietInstCmd=
SourceFiles=SourceFiles
FILE0="izekip.zip"
FILE1="kur.cmd"
[SourceFiles]
SourceFiles0={stage}\\
[SourceFiles0]
%FILE0%=
%FILE1%=
"""
        with open(sed_path, "w", encoding="mbcs") as f:
            f.write(sed)

        if os.path.exists(out_path):
            os.remove(out_path)
        result = subprocess.run(["iexpress", "/N", "/Q", sed_path])
        if result.returncode != 0 or not os.path.exists(out_path):
            print("IExpress basarisiz oldu, kod:", result.returncode)
            sys.exit(1)
        print("olusturuldu:", out_path, f"({os.path.getsize(out_path) / 1_000_000:.1f} MB)")


if __name__ == "__main__":
    main()
