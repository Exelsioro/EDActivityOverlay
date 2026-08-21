# Third-party data

## Canonn Bioforge exobiology histograms

The files in `ED_Inara_Overlay/Resources/ExobiologyBioforge` are a snapshot of the
exobiology histogram data distributed by
[Elite Dangerous Warboard](https://github.com/Mirooz/EliteDangerousWarboard),
which attributes the underlying observations to
[Canonn Bioforge](https://bioforge.canonn.tech/).

Elite Dangerous Warboard is distributed under the MIT License:

> Copyright (c) 2025 Elite Dangerous Warboard contributors
>
> Permission is hereby granted, free of charge, to any person obtaining a copy
> of this software and associated documentation files (the "Software"), to deal
> in the Software without restriction, including without limitation the rights
> to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
> copies of the Software, and to permit persons to whom the Software is
> furnished to do so, subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in all
> copies or substantial portions of the Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
> IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
> FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
> AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
> LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
> OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
> SOFTWARE.

The prediction implementation in this project is native C# and treats these
histograms as statistical hints. It does not claim that a species exists until
Elite Dangerous reports a genus or an organic scan in the Journal.
