import React from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './App';
import './styles.css';
import { initializeEntraAuth } from './entraAuth';

async function startApplication() {
  try {
    await initializeEntraAuth();
  } catch (error) {
    // Keep the sign-in page visible so a misconfigured deployment reports a
    // clear authentication error instead of rendering a blank screen.
    console.error('Microsoft Entra initialization failed.', error);
  }
  createRoot(document.getElementById('root')!).render(<React.StrictMode><App /></React.StrictMode>);
}

void startApplication();
