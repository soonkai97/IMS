#!/usr/bin/env node
// commit-msg hook: Conventional Commits, no AI co-author trailer.
import fs from 'node:fs';
import { checkCommitMessage, report } from './lib.mjs';

const message = fs.readFileSync(process.argv[2], 'utf8').split('\n').filter((l) => !l.startsWith('#')).join('\n').trim();
process.exit(report('Commit message rejected:', checkCommitMessage(message)));
