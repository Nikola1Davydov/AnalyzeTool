//! The stdio loop. One JSON request per input line, one JSON response per output line.
//!
//! stdout carries the protocol and nothing else — anything diagnostic goes to stderr, which the
//! host drains into its own log. A stray `println!` here would corrupt the stream, so there is
//! exactly one write site.

use std::io::{self, BufRead, Write};
use std::process::ExitCode;

use analysetool_dwg::{handle_line, PROTOCOL_VERSION};

fn main() -> ExitCode {
    let mut args = std::env::args().skip(1);
    match args.next().as_deref() {
        None => serve(),
        Some("--version") => {
            println!(
                "{} {} (protocol {})",
                env!("CARGO_PKG_NAME"),
                env!("CARGO_PKG_VERSION"),
                PROTOCOL_VERSION
            );
            ExitCode::SUCCESS
        }
        // One-shot mode, for a shell and for CI: the same dispatch the pipe uses, one line in, one
        // line out. Kept deliberately thin so it cannot drift from the served protocol.
        Some("--once") => match args.next() {
            Some(line) => {
                let Some(response) = handle_line(&line) else {
                    eprintln!("--once needs a JSON request");
                    return ExitCode::FAILURE;
                };
                println!("{response}");
                ExitCode::SUCCESS
            }
            None => {
                eprintln!("--once needs a JSON request, e.g. --once '{{\"op\":\"ping\"}}'");
                ExitCode::FAILURE
            }
        },
        Some(other) => {
            eprintln!(
                "unknown argument '{other}' — run with no arguments to serve on stdio, or --once <json>"
            );
            ExitCode::FAILURE
        }
    }
}

fn serve() -> ExitCode {
    let stdin = io::stdin();
    let mut stdout = io::stdout().lock();

    for line in stdin.lock().lines() {
        let line = match line {
            Ok(line) => line,
            // The host went away mid-line. Nothing left to answer to.
            Err(e) => {
                eprintln!("reading stdin failed: {e}");
                return ExitCode::FAILURE;
            }
        };

        let Some(response) = handle_line(&line) else {
            continue;
        };

        // Flush per response: the host is a synchronous request/response client, so a buffered
        // answer is a deadlock, not a latency problem.
        if writeln!(stdout, "{response}").is_err() || stdout.flush().is_err() {
            return ExitCode::FAILURE;
        }
    }

    ExitCode::SUCCESS
}
