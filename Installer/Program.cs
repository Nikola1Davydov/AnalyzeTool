﻿using System;
using System.Windows.Forms;
using WixSharp;
using WixSharp.Forms;

namespace Installer
{
    public class Program
    {
        static void Main()
        {
            var project = new ManagedProject("AnalyseTool",
                             new Dir(@"%ProgramFiles%\My Company\My Product",
                                 new File("Program.cs")));

            project.GUID = new Guid("004264d8-7fcd-47c8-837b-8861532b0b08");

            //project.ManagedUI = ManagedUI.Empty;    //no standard UI dialogs
            project.ManagedUI = ManagedUI.Default;  //all standard UI dialogs

            //custom set of standard UI dialogs
            project.ManagedUI = new ManagedUI();

            project.ManagedUI.InstallDialogs.Add(Dialogs.Welcome)
                                            .Add(Dialogs.Licence)
                                            .Add(Dialogs.SetupType)
                                            .Add(Dialogs.Features)
                                            .Add(Dialogs.InstallDir)
                                            .Add(Dialogs.Progress)
                                            .Add(Dialogs.Exit);

            project.ManagedUI.ModifyDialogs.Add(Dialogs.MaintenanceType)
                                           .Add(Dialogs.Features)
                                           .Add(Dialogs.Progress)
                                           .Add(Dialogs.Exit);

            project.BuildMsi();
        }
    }
}