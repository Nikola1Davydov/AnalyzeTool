import {
  Badge,
  Button,
  Menu,
  MenuItem,
  MenuList,
  MenuPopover,
  MenuTrigger,
  MessageBar,
  MessageBarBody,
  Tab,
  TabList,
  Text,
} from "@fluentui/react-components";
import {
  Apps16Regular,
  ChartMultiple16Regular,
  Info16Regular,
  MoreHorizontal16Regular,
  PuzzlePiece16Regular,
  Settings16Regular,
  TableSimple16Regular,
  TextBulletListSquare16Regular,
} from "@fluentui/react-icons";
import { bridge } from "revit-bridge";
import { type AppInfo, appCommands, openWindow } from "../../api/app";
import { errorText, useNotify } from "../../components/notify";
import { useActiveDocument, useAppQuery } from "../../revit/hooks";
import { navigate } from "../../router";
import { ElementsPage } from "./elements/ElementsPage";
import { ReportPage } from "./report/ReportPage";
import { SharedParametersPage } from "./shared/SharedParametersPage";
import { SharedParametersProvider } from "./shared/SharedParametersContext";

const tabs = [
  { path: "/", label: "Shared parameters", icon: <TextBulletListSquare16Regular /> },
  { path: "/report", label: "Report", icon: <ChartMultiple16Regular /> },
  { path: "/elements", label: "Elements", icon: <TableSimple16Regular /> },
];

/**
 * The main AnalyseTool window: the tools as tabs. The shared parameter state lives above the tabs, so the Report reads
 * the selection made on Shared parameters and unsaved edits survive a switch.
 */
export function MainWindow({ path, query }: { path: string; query: URLSearchParams }) {
  const document = useActiveDocument();
  const info = useAppQuery<AppInfo>(appCommands.GetAppInfo);
  const notify = useNotify();
  const open = (route: string) => openWindow(route).catch((error) => notify.failed(error, "Could not open the window"));

  return (
    <SharedParametersProvider>
      <div className="main-window">
        <header className="main-header no-print">
          <Text weight="bold" className="brand">
            AnalyseTool
          </Text>
          <TabList size="small" selectedValue={path} onTabSelect={(_, data) => navigate(String(data.value))}>
            {tabs.map((tab) => (
              <Tab key={tab.path} value={tab.path} icon={tab.icon}>
                {tab.label}
              </Tab>
            ))}
          </TabList>
          <span className="spacer" />
          {document.data && (
            <Text className="muted small truncate document-title" title={document.data.key}>
              {document.data.title}
            </Text>
          )}
          {!bridge.inRevit && (
            <Badge appearance="tint" color="informative" size="small">
              mock
            </Badge>
          )}
          <Menu>
            <MenuTrigger disableButtonEnhancement>
              <Button appearance="subtle" size="small" icon={<MoreHorizontal16Regular />} aria-label="More" />
            </MenuTrigger>
            <MenuPopover>
              <MenuList>
                <MenuItem icon={<PuzzlePiece16Regular />} onClick={() => void open("/extensions")}>
                  Extensions
                </MenuItem>
                <MenuItem icon={<Apps16Regular />} onClick={() => void open("/extensions/new")}>
                  New button
                </MenuItem>
                <MenuItem icon={<Settings16Regular />} onClick={() => void open("/settings")}>
                  Settings
                </MenuItem>
                <MenuItem icon={<Info16Regular />} onClick={() => void open("/about")}>
                  About
                </MenuItem>
              </MenuList>
            </MenuPopover>
          </Menu>
        </header>

        {document.isError && (
          <MessageBar intent="error">
            <MessageBarBody>{errorText(document.error)}</MessageBarBody>
          </MessageBar>
        )}
        {document.isSuccess && !document.data && (
          <MessageBar intent="info">
            <MessageBarBody>No Revit project is open — open one to work with its parameters.</MessageBarBody>
          </MessageBar>
        )}

        <div className="main-content">
          {path === "/report" ? (
            <ReportPage fromSelection={query.get("from") === "selection"} />
          ) : path === "/elements" ? (
            <ElementsPage />
          ) : (
            <SharedParametersPage />
          )}
        </div>

        <footer className="main-footer no-print">
          {info.data && (
            <span className="muted small">
              AnalyseTool {info.data.version} · Revit {info.data.revitVersion}
            </span>
          )}
        </footer>
      </div>
    </SharedParametersProvider>
  );
}
