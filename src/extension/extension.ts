'use strict';

import { exec, ChildProcess } from 'child_process';
import * as path from 'path';
import * as semver from 'semver';
import * as vscode from 'vscode';
import { LanguageClient, ServerOptions, LanguageClientOptions, ErrorAction, CloseAction, RevealOutputChannelOn } from 'vscode-languageclient';
import { Trace } from 'vscode-jsonrpc/lib/main';

import * as dotnet from './utils/dotnet';
import * as executables from './utils/executables';
import { handleBusyNotifications } from './notifications';
import { registerCommands } from './commands';
import { registerInternalCommands } from './internal-commands';
import { Settings, upgradeConfigurationSchema, readVSCodeSettings } from './settings';

let configuration: Settings;
let languageClient: LanguageClient;
let statusBarItem: vscode.StatusBarItem;
let outputChannel: vscode.OutputChannel;

const featureFlags = new Set<string>();
const languageServerEnvironment = Object.assign({}, process.env);

const projectDocumentSelector: vscode.DocumentSelector = [
    { language: 'xml', pattern: '**/*.*proj' },
    { language: 'xml', pattern: '**/*.props' },
    { language: 'xml', pattern: '**/*.targets' },
    { language: 'xml', pattern: '**/*.tasks' },
    { language: 'msbuild', pattern: '**/*.*' }
];

/**
 * Called when the extension is activated.
 * 
 * @param context The extension context.
 */
export async function activate(context: vscode.ExtensionContext): Promise<void> {
    outputChannel = vscode.window.createOutputChannel('MSBuild Project Tools');

    const progressOptions: vscode.ProgressOptions = {
        location: vscode.ProgressLocation.Window
    };
    await vscode.window.withProgress(progressOptions, async progress => {
        progress.report({
            message: 'Initialising MSBuild project tools...'
        });

        await loadConfiguration();

        const dotnetVersion: string = await dotnet.getHostVersion();
        const canEnableLanguageService: Boolean = dotnetVersion && semver.gte(dotnetVersion, '5.0.0');

        if (canEnableLanguageService) {
            await createLanguageClient(context);

            context.subscriptions.push(
                handleExpressionAutoClose()
            );

            registerCommands(context, statusBarItem);
            registerInternalCommands(context);
        } else {
            outputChannel.appendLine(`Cannot enable the MSBuild language service ('${dotnetVersion}' is not a supported version of the .NET Core runtime).`);
        }
    });

    context.subscriptions.push(
        vscode.workspace.onDidChangeConfiguration(async args => {
            await loadConfiguration();

            if (languageClient) {
                if (configuration.logging.trace) {
                    languageClient.trace = Trace.Verbose;
                } else {
                    languageClient.trace = Trace.Off;
                }
            }
        })
    );
}

/**
 * Called when the extension is deactivated.
 */
export function deactivate(): void {
    // Nothing to clean up.
}

/**
 * Load extension configuration from the workspace.
 */
async function loadConfiguration(): Promise<void> {
    const workspaceConfiguration = vscode.workspace.getConfiguration();

    configuration = workspaceConfiguration.get('msbuildProjectTools');
    
    await upgradeConfigurationSchema(configuration);

    configuration = readVSCodeSettings(configuration);

    featureFlags.clear();
    if (configuration.experimentalFeatures) {
        configuration.experimentalFeatures.forEach(
            featureFlag => featureFlags.add(featureFlag)
        );
    }
}

/**
 * Create the MSBuild language client.
 * 
 * @param context The current extension context.
 * @returns A promise that resolves to the language client.
 */
async function createLanguageClient(context: vscode.ExtensionContext): Promise<void> {
    statusBarItem = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Right, 50);
    context.subscriptions.push(statusBarItem);

    statusBarItem.text = '$(check) MSBuild Project';
    statusBarItem.tooltip = 'MSBuild Project Tools';
    statusBarItem.hide();

    outputChannel.appendLine('Starting MSBuild language service...');

    const clientOptions: LanguageClientOptions = {
        synchronize: {
            configurationSection: 'msbuildProjectTools'
        },
        diagnosticCollectionName: 'MSBuild Project',
        errorHandler: {
            error: (error, message, count) => {
                if (count > 2)  // Don't be annoying
                    return ErrorAction.Shutdown;

                console.log(message);
                console.log(error);

                if (message)
                    outputChannel.appendLine(`The MSBuild language server encountered an unexpected error: ${message}\n\n${error}`);
                else
                    outputChannel.appendLine(`The MSBuild language server encountered an unexpected error.\n\n${error}`);

                return ErrorAction.Continue;
            },
            closed: () => CloseAction.DoNotRestart
        },
        initializationFailedHandler(error: Error) : boolean {
            console.log(error);
            
            outputChannel.appendLine(`Failed to initialise the MSBuild language server.\n\n${error}`);
            vscode.window.showErrorMessage(`Failed to initialise MSBuild language server.\n\n${error}`);

            return false; // Don't attempt to restart the language server.
        },
        revealOutputChannelOn: RevealOutputChannelOn.Never
    };

    languageServerEnvironment['MSBUILD_PROJECT_TOOLS_DIR'] = context.extensionPath;

    const seqLoggingSettings = configuration.logging.seq;
    if (seqLoggingSettings && seqLoggingSettings.url) {
        languageServerEnvironment['MSBUILD_PROJECT_TOOLS_SEQ_URL'] = seqLoggingSettings.url;
        languageServerEnvironment['MSBUILD_PROJECT_TOOLS_SEQ_API_KEY'] = seqLoggingSettings.apiKey;
    }

    if (configuration.logging.file) {
        languageServerEnvironment['MSBUILD_PROJECT_TOOLS_LOG_FILE'] = configuration.logging.file;
    }

    if (configuration.logging.level === 'Verbose') {
        languageServerEnvironment['MSBUILD_PROJECT_TOOLS_VERBOSE_LOGGING'] = '1';
    }

    const dotNetExecutable = await executables.find('dotnet');
    const serverAssembly = context.asAbsolutePath('out/language-server/MSBuildProjectTools.LanguageServer.Host.dll');

    // Probe language server (see if it can start at all).
    const serverProbeSuccess: boolean = await probeLanguageServer(dotNetExecutable, serverAssembly);
    if (!serverProbeSuccess) {
        vscode.window.showErrorMessage('Unable to start MSBuild language server (see the output window for details).');

        return;
    }

    const serverOptions: ServerOptions = {
        command: dotNetExecutable,
        args: [serverAssembly],
        options: {
            env: languageServerEnvironment
        }
    };

    languageClient = new LanguageClient('MSBuild Project Tools', serverOptions, clientOptions);
    if (configuration.logging.trace) {
        languageClient.trace = Trace.Verbose;
    } else {
        languageClient.trace = Trace.Off;
    }

    handleBusyNotifications(languageClient, statusBarItem);

    let languageClientDisposal : vscode.Disposable;
    try {
        languageClientDisposal = languageClient.start();
        context.subscriptions.push(languageClientDisposal);
    }
    catch (startFailed) {
        outputChannel.appendLine(`Failed to start MSBuild language service.\n\n${startFailed}`);
        languageClientDisposal.dispose();

        return;
    }

    try {
        await languageClient.onReady();
    } catch (onReadyFailed) {
        outputChannel.appendLine(`Failed to start MSBuild language server.\n\n${onReadyFailed}`);
        languageClientDisposal.dispose();

        return;
    }

    outputChannel.appendLine('MSBuild language service is running.');
}

/**
 * Attempt to start the language server process in probe mode (to see if it can be started at all).
 * 
 * @param dotNetExecutable The full path to the .NET Core host executable ("dotnet" or "dotnet.exe").
 * @param serverAssembly The full path to the language server host assembly.
 */
function probeLanguageServer(dotNetExecutable: string, serverAssembly: string): Promise<boolean> {
    return new Promise(resolve => {
        let serverError: Error;
        let serverStdOut: string;
        let serverStdErr: string;

        const languageServerProcess = exec(`"${dotNetExecutable}" "${serverAssembly}" --probe`, (error, stdout, stderr) => {
            serverError = error;
            serverStdOut = stdout;
            serverStdErr = stderr;
        });

        languageServerProcess.on('close', exitCode => {
            if (!serverError && exitCode === 0) {
                resolve(true);

                return;
            }

            console.log("Failed to start language server.");
            outputChannel.appendLine('Failed to start the MSBuild language server.');

            if (serverError) {
                console.log(serverError);
                outputChannel.appendLine(
                    serverError.toString()
                );
            }

            if (serverStdOut) {
                console.log(serverStdOut);
                outputChannel.appendLine(serverStdOut);
            }

            if (serverStdErr) {
                console.log(serverStdErr);
                outputChannel.appendLine(serverStdErr);
            }

            resolve(false);
        });
    });
}

/**
 * Handle document-change events to automatically insert a closing parenthesis for common MSBuild expressions.
 */
function handleExpressionAutoClose(): vscode.Disposable {
    return vscode.workspace.onDidChangeTextDocument(async args => {
        if (!vscode.languages.match(projectDocumentSelector, args.document))
            return;

        if (!featureFlags.has('expressions'))
            return;

        if (args.contentChanges.length !== 1)
            return; // Completion doesn't make sense with multiple cursors.

        const contentChange = args.contentChanges[0];
        if (isOriginPosition(contentChange.range.start))
            return; // We're at the start of the document; no previous character to check.

        if (contentChange.text === '(') {
            // Select the previous character and the one they just typed.
            const range = contentChange.range.with(
                contentChange.range.start.translate(0, -1),
                contentChange.range.end.translate(0, 1)
            );

            const openExpression = args.document.getText(range);
            switch (openExpression) {
                case '$(': // Eval open
                case '@(': // Item group open
                case '%(': // Item metadata open
                    {
                        break;
                    }
                default:
                    {
                        return;
                    }
            }

            // Replace open expression with a closed one.
            const closedExpression = openExpression + ')';
            await vscode.window.activeTextEditor.edit(
                edit => edit.replace(range, closedExpression)
            );

            // Move between the parentheses and trigger completion.
            await vscode.commands.executeCommand('msbuildProjectTools.internal.moveAndSuggest',
                'left',      // moveTo
                'character', // moveBy
                1            // moveCount
            );
        }
    });
}

/**
 * Determine whether the specified {@link vscode.Position} represents the origin position.
 * 
 * @param position The {@link vscode.Position} to examine.
 */
function isOriginPosition(position: vscode.Position): boolean {
    return position.line === 0 && position.character === 0;
}
